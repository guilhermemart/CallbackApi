# Redis AOF e snapshot PostgreSQL

## Fluxo

```text
POST /v1/event
      |
      v
Redis Stream (AOF)
      |
      v
PostgresSnapshotWorker
      |
      v
PostgreSQL.events
```

O endpoint confirma a requisição somente depois que o evento entra no Redis
Stream. O worker grava o evento no PostgreSQL e confirma a mensagem no Redis
somente após a gravação terminar.

Cada evento usa um `Id` único como chave primária no PostgreSQL. Se o worker
falhar depois de salvar o evento e antes de confirmar a mensagem no Redis, a
próxima tentativa encontra o mesmo `Id` e não insere uma duplicata.

Esse fluxo oferece processamento pelo menos uma vez e persistência idempotente
no PostgreSQL. Ele não substitui backups do PostgreSQL.

## Subir a infraestrutura local

Com Docker Desktop em execução, crie os containers na primeira execução:

```powershell
docker build -t callback-postgres .\docker\postgresql
docker run -d --name callback-postgres `
  -e POSTGRES_DB=callbackdb `
  -e POSTGRES_USER=callback `
  -e POSTGRES_PASSWORD=callbackpass `
  -p 5432:5432 `
  -v postgres_data:/var/lib/postgresql/data `
  callback-postgres
```

```powershell
docker build -t callback-redis .\docker\redis
docker run -d --name callback-redis `
  -p 6379:6379 `
  -v callback_redis_data:/data `
  callback-redis
```

Nas execuções seguintes, inicie os containers existentes:

```powershell
docker start callback-postgres callback-redis
```

Depois de os dois containers estarem em execução, aplique ou confirme as migrations:

```powershell
.\.tools\dotnet-ef.exe database update `
  --project .\src\CallbackApi\CallbackApi.csproj `
  --startup-project .\src\CallbackApi\CallbackApi.csproj
```

Inicie a API no perfil HTTP de desenvolvimento:

```powershell
dotnet run --project .\src\CallbackApi\CallbackApi.csproj --launch-profile http
```

A API fica disponível em `http://localhost:5153`. Em outro terminal, verifique a
inicialização:

```powershell
Invoke-RestMethod http://localhost:5153/v1/health
```

Para verificar as conexões com PostgreSQL e Redis, use o readiness check:

```powershell
Invoke-RestMethod http://localhost:5153/v1/health/ready
```

No ambiente `Development`, o Swagger está disponível em
`http://localhost:5153/swagger`.

## Executar a API em Docker

Crie a imagem a partir da raiz do repositório:

```powershell
docker build -f .\docker\callBackApi\Dockerfile -t callback-api .
```

Com Redis e PostgreSQL publicados nas portas locais padrão, execute:

```powershell
docker run --rm --name callback-api -p 8080:8080 `
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=callbackdb;Username=callback;Password=callbackpass" `
  -e ConnectionStrings__Redis="host.docker.internal:6379,abortConnect=false" `
  callback-api
```

A API fica disponível em `http://localhost:8080`. O uso de
`host.docker.internal` permite que o container acesse a infraestrutura Docker
publicada no host durante o desenvolvimento local.

O entrypoint da imagem aguarda o PostgreSQL aceitar conexões TCP antes de
iniciar a API. O tempo máximo padrão é 60 segundos e pode ser alterado com
`POSTGRES_WAIT_TIMEOUT_SECONDS`.

O Redis está configurado com AOF e `appendfsync everysec`, além de snapshots
RDB. Isso reduz a janela de perda de dados, mas não a elimina em falhas
abruptas.
