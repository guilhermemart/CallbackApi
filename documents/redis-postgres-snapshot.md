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

Em terminais separados:

```powershell
cd docker/postgresql
make build
make run
```

```powershell
cd docker/redis
make build
make run
```

Depois de os dois containers estarem em execução, aplique a migration:

```powershell
.\.tools\dotnet-ef.exe database update `
  --project .\src\CallbackApi\CallbackApi.csproj `
  --startup-project .\src\CallbackApi\CallbackApi.csproj
```

O Redis está configurado com AOF e `appendfsync everysec`, além de snapshots
RDB. Isso reduz a janela de perda de dados, mas não a elimina em falhas
abruptas.
