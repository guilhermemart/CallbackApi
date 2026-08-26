# CallbackApi no Kind

## Execução recomendada: Ingress

Este modo recria o cluster para publicar as portas HTTP e HTTPS no Windows.

```powershell
kind delete cluster --name callbackapi
kind create cluster --name callbackapi --config .\k8s\kind-config.yaml --wait 5m

docker build -f .\docker\callBackApi\Dockerfile -t callback-api:local .
kind load docker-image callback-api:local --name callbackapi
kubectl apply -f .\k8s\namespace.yaml -f .\k8s\postgres.yaml -f .\k8s\redis.yaml -f .\k8s\migration-job.yaml -f .\k8s\api.yaml
kubectl wait --for=condition=complete job/callbackapi-migrations -n callbackapi --timeout=5m
kubectl rollout status deployment/callback-api -n callbackapi --timeout=5m
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.15.1/deploy/static/provider/kind/deploy.yaml
kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=5m
kubectl apply -f .\k8s\ingress.yaml
```

Use `http://localhost/v1/health/ready`.

Estes manifests criam PostgreSQL e Redis persistentes, executam as migrations e iniciam a API.

As senhas `callback` e `callbackpass` servem apenas para o ambiente didático local. Não use estes valores fora dele.

## Execução

```powershell
docker build -f .\docker\callBackApi\Dockerfile -t callback-api:local .
kind load docker-image callback-api:local --name callbackapi
kubectl apply -f .\k8s\namespace.yaml -f .\k8s\postgres.yaml -f .\k8s\redis.yaml -f .\k8s\migration-job.yaml -f .\k8s\api.yaml
kubectl wait --for=condition=complete job/callbackapi-migrations -n callbackapi --timeout=5m
kubectl rollout status deployment/callback-api -n callbackapi --timeout=5m
kubectl port-forward -n callbackapi service/callback-api 8080:80
```

Com o port-forward em execução, use `http://localhost:8080/v1/health/ready`.
