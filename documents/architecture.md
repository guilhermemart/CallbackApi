# CallbackApi architecture

CallbackApi is a Go HTTP API for receiving and querying callback events. The
input contract is documented in `callback-contract.md`.

## Components

- `main.go` starts the HTTP server and provides liveness and readiness checks.
- `event.go` validates event requests, manages current state in Redis, and runs
  the PostgreSQL snapshot worker.
- `--migrate` creates the PostgreSQL `events` table.

## Event flow

1. The API validates an event and stores its current state in Redis.
2. It writes the event or requested operation to a Redis stream.
3. A background worker consumes both streams and applies snapshots to
   PostgreSQL.

The save endpoint returns after writing to Redis. PostgreSQL persistence is
asynchronous. Set `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_USER`,
`POSTGRES_PASSWORD`, and `POSTGRES_DB` for PostgreSQL. Redis uses
`REDIS_ADDRESS` and optional `REDIS_PASSWORD`; the server listens on
`HTTP_ADDRESS` (default `:8080`).
