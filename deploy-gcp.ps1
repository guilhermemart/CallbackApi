param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z][a-z0-9-]{4,28}[a-z0-9]$')]
    [string]$ProjectId,

    [string]$Zone = 'us-central1-a'
)

$ErrorActionPreference = 'Stop'
$instanceName = 'callback-api-prod'
$networkName = 'callback-api-net'
$subnetName = 'callback-api-subnet'
$startupScript = Join-Path $PSScriptRoot 'deploy\gcp-startup.sh'
$finishScript = Join-Path $PSScriptRoot 'deploy\gcp-finish.sh'
$frontendPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'CallbackFrontEnd'
$archivePath = Join-Path $env:TEMP 'callback-api-gcp-deploy.tar.gz'

$gcloudCommand = Join-Path $env:LOCALAPPDATA 'Google\CloudSDK\google-cloud-sdk\bin\gcloud.cmd'
if (-not (Test-Path -LiteralPath $gcloudCommand)) {
    $gcloudCommand = Join-Path $env:LOCALAPPDATA 'Google\CloudSDKFresh\google-cloud-sdk\bin\gcloud.cmd'
}
if (-not (Test-Path -LiteralPath $gcloudCommand)) {
    $gcloudFromPath = Get-Command gcloud.cmd -ErrorAction SilentlyContinue
    if ($gcloudFromPath) { $gcloudCommand = $gcloudFromPath.Source }
}

function Test-GcloudResourceExists {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $gcloudCommand @Arguments 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
}

if (-not (Test-Path -LiteralPath $gcloudCommand)) {
    throw 'Google Cloud CLI (gcloud) was not found. Install it and reopen PowerShell.'
}
if (-not (Test-Path -LiteralPath $startupScript) -or -not (Test-Path -LiteralPath $finishScript)) {
    throw 'A required GCP deployment script is missing from deploy/.'
}
if (-not (Test-Path -LiteralPath $frontendPath)) {
    throw "Frontend workspace not found: $frontendPath"
}
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw 'tar.exe was not found. Install Windows tar or add it to PATH.'
}

$activeAccount = & $gcloudCommand auth list --filter=status:ACTIVE --format='value(account)'
if ($LASTEXITCODE -ne 0 -or -not $activeAccount) {
    throw 'Authenticate first with: gcloud auth login'
}

& $gcloudCommand services enable compute.googleapis.com iap.googleapis.com --project=$ProjectId
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the required GCP APIs.' }

if (-not (Test-GcloudResourceExists @('compute', 'networks', 'describe', $networkName, "--project=$ProjectId", '--format=value(name)'))) {
    & $gcloudCommand compute networks create $networkName --subnet-mode=custom --project=$ProjectId
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the deployment VPC.' }
}

if (-not (Test-GcloudResourceExists @('compute', 'networks', 'subnets', 'describe', $subnetName, '--region=us-central1', "--project=$ProjectId", '--format=value(name)'))) {
    & $gcloudCommand compute networks subnets create $subnetName --network=$networkName --region=us-central1 --range=10.20.0.0/24 --project=$ProjectId
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the deployment subnet.' }
}

$firewallRules = @(
    @{ Name = 'callback-api-web'; Source = '0.0.0.0/0'; Ports = 'tcp:80,tcp:443' },
    @{ Name = 'callback-api-ssh-iap'; Source = '35.235.240.0/20'; Ports = 'tcp:22' }
)
foreach ($rule in $firewallRules) {
    if (-not (Test-GcloudResourceExists @('compute', 'firewall-rules', 'describe', $rule.Name, "--project=$ProjectId", '--format=value(name)'))) {
        & $gcloudCommand compute firewall-rules create $rule.Name --network=$networkName --direction=INGRESS --action=ALLOW "--rules=$($rule.Ports)" "--source-ranges=$($rule.Source)" --target-tags=callback-api --project=$ProjectId
        if ($LASTEXITCODE -ne 0) { throw "Could not create firewall rule $($rule.Name)." }
    }
}

$instanceExists = Test-GcloudResourceExists @('compute', 'instances', 'describe', $instanceName, "--zone=$Zone", "--project=$ProjectId", '--format=value(name)')
if (-not $instanceExists) {
    & $gcloudCommand compute instances create $instanceName `
        --project=$ProjectId `
        --zone=$Zone `
        --machine-type=e2-micro `
        --network=$networkName `
        --subnet=$subnetName `
        --boot-disk-size=30GB `
        --boot-disk-type=pd-standard `
        --boot-disk-device-name=$instanceName `
        --image-family=debian-12 `
        --image-project=debian-cloud `
        --tags=callback-api `
        --metadata-from-file="startup-script=$startupScript" `
        --no-service-account `
        --no-scopes
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the Compute Engine VM.' }
}
else {
    Write-Host "Using existing VM $instanceName in $Zone."
    & $gcloudCommand compute instances add-metadata $instanceName --zone=$Zone --project=$ProjectId --metadata-from-file="startup-script=$startupScript"
    if ($LASTEXITCODE -ne 0) { throw 'Could not update the VM startup script.' }
}

$publicIp = & $gcloudCommand compute instances describe $instanceName --zone=$Zone --project=$ProjectId --format='get(networkInterfaces[0].accessConfigs[0].natIP)'
if ($LASTEXITCODE -ne 0 -or -not $publicIp) { throw 'Could not determine the VM public IP address.' }

$parentPath = Split-Path $PSScriptRoot -Parent
$apiFolderName = Split-Path $PSScriptRoot -Leaf
& tar.exe -czf $archivePath `
    "--exclude=$apiFolderName/.git" `
    "--exclude=$apiFolderName/.env" `
    "--exclude=$apiFolderName/callbackapi.exe" `
    '--exclude=CallbackFrontEnd/.git' `
    '--exclude=CallbackFrontEnd/node_modules' `
    '--exclude=CallbackFrontEnd/dist' `
    -C $parentPath $apiFolderName 'CallbackFrontEnd'
if ($LASTEXITCODE -ne 0) { throw 'Could not package the API and frontend sources.' }

& $gcloudCommand compute scp $archivePath $finishScript "${instanceName}:/tmp/" --zone=$Zone --project=$ProjectId --tunnel-through-iap --ssh-key-expire-after=1h
if ($LASTEXITCODE -ne 0) { throw 'Could not upload the application bundle to the VM.' }

& $gcloudCommand compute ssh $instanceName --zone=$Zone --project=$ProjectId --tunnel-through-iap --ssh-key-expire-after=1h --command='sudo bash /tmp/gcp-finish.sh'
if ($LASTEXITCODE -ne 0) { throw 'Remote Compose deployment failed.' }

Write-Host "Deployment finished. Open https://$publicIp/"
