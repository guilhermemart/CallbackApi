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

if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    throw 'Google Cloud CLI (gcloud) was not found. Install it and reopen PowerShell.'
}
if (-not (Test-Path -LiteralPath $startupScript)) {
    throw "Startup script not found: $startupScript"
}

$activeAccount = & gcloud auth list --filter=status:ACTIVE --format='value(account)'
if ($LASTEXITCODE -ne 0 -or -not $activeAccount) {
    throw 'Authenticate first with: gcloud auth login'
}

& gcloud services enable compute.googleapis.com iap.googleapis.com --project=$ProjectId
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the required GCP APIs.' }

& gcloud compute networks describe $networkName --project=$ProjectId --format='value(name)' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & gcloud compute networks create $networkName --subnet-mode=custom --project=$ProjectId
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the deployment VPC.' }
}

& gcloud compute networks subnets describe $subnetName --region=us-central1 --project=$ProjectId --format='value(name)' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & gcloud compute networks subnets create $subnetName --network=$networkName --region=us-central1 --range=10.20.0.0/24 --project=$ProjectId
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the deployment subnet.' }
}

$firewallRules = @(
    @{ Name = 'callback-api-web'; Source = '0.0.0.0/0'; Ports = 'tcp:80,tcp:443' },
    @{ Name = 'callback-api-ssh-iap'; Source = '35.235.240.0/20'; Ports = 'tcp:22' }
)
foreach ($rule in $firewallRules) {
    & gcloud compute firewall-rules describe $rule.Name --project=$ProjectId --format='value(name)' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & gcloud compute firewall-rules create $rule.Name --network=$networkName --direction=INGRESS --action=ALLOW --rules=$rule.Ports --source-ranges=$rule.Source --target-tags=callback-api --project=$ProjectId
        if ($LASTEXITCODE -ne 0) { throw "Could not create firewall rule $($rule.Name)." }
    }
}

& gcloud compute instances describe $instanceName --zone=$Zone --project=$ProjectId --format='value(name)' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    throw "Instance $instanceName already exists in $Zone. Inspect it before rerunning this script."
}

& gcloud compute instances create $instanceName `
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
    --no-service-account
if ($LASTEXITCODE -ne 0) { throw 'Could not create the Compute Engine VM.' }

& gcloud compute instances describe $instanceName --zone=$Zone --project=$ProjectId --format='get(networkInterfaces[0].accessConfigs[0].natIP)'
