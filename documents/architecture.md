# Arquitetura do CallbackApi

O CallbackApi é uma API ASP.NET Core para receber e consultar eventos de
callback. O contrato de entrada está em `callback-contract.md`.

## Componentes

- `Features/Events`: endpoints e regra de negócio de eventos.
- `Features/Health`: liveness (`GET /v1/health`) e readiness com leitura no
  PostgreSQL e `PING` no Redis (`GET /v1/health/ready`).
- `Features/Errors`: resposta padronizada para exceções não tratadas.
- `Infrastructure/Redis`: estado atual e streams de operação.
- `Infrastructure/Persistence`: contexto EF Core, migrations e worker de
  persistência no PostgreSQL.

## Fluxo de eventos

1. A API valida `EventInput` e grava o estado atual no Redis.
2. O evento ou a operação é publicada em um Redis Stream.
3. `PostgresSnapshotWorker` consome o stream e aplica a alteração no
   PostgreSQL.

O endpoint de criação confirma o recebimento depois que o evento é colocado
no Redis; a persistência no PostgreSQL ocorre de forma assíncrona.
