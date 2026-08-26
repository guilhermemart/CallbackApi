# CallbackApi no Kind

Estes manifests criam PostgreSQL e Redis persistentes, executam as migrations e iniciam a API.

As senhas `callback` e `callbackpass` servem apenas para o ambiente didático local. Não use estes valores fora dele.

## Execução

```powershell
docker build -f .\docker\callBackApi\Dockerfile -t callback-api:local .
kind load docker-image callback-api:local --name callbackapi
kubectl apply -f .\k8s\
kubectl wait --for=condition=complete job/callbackapi-migrations -n callbackapi --timeout=5m
kubectl rollout status deployment/callback-api -n callbackapi --timeout=5m
kubectl port-forward -n callbackapi service/callback-api 8080:80
```

Com o port-forward em execução, use `http://localhost:8080/v1/health/ready`.
