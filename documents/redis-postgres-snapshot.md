# Redis e snapshots PostgreSQL

## Fluxo

```text
POST /v1/event/save
        |
        v
  Estado e stream Redis
        |
        v
 Worker Go da API
        |
        v
 PostgreSQL.events
```

A API responde depois de gravar o evento no Redis. Um worker consome os streams
e aplica as gravações e operações no PostgreSQL.

## Subir a infraestrutura e a API com Docker

Execute os comandos a partir da raiz do repositório, com o Docker Desktop em
execução. Crie a rede e os volumes uma vez:

```powershell
docker network create callbackapi-net
docker volume create callbackapi-postgres-data
docker volume create callbackapi-redis-data
```

Compile as imagens:

```powershell
docker build -t callback-api:local -f .\docker\callBackApi\Dockerfile .
docker build -t callback-postgres:local -f .\docker\postgresql\dockerfile .\docker\postgresql
docker build -t callback-redis:local -f .\docker\redis\Dockerfile .\docker\redis
```

Inicie PostgreSQL e Redis:

```powershell
docker run -d --name callback-postgres --network callbackapi-net --network-alias postgres `
  --restart unless-stopped -e POSTGRES_DB=callbackdb -e POSTGRES_USER=callback `
  -e POSTGRES_PASSWORD=callbackpass -e POSTGRES_HOST_AUTH_METHOD=scram-sha-256 `
  -p 127.0.0.1:5432:5432 -v callbackapi-postgres-data:/var/lib/postgresql/data `
  callback-postgres:local

docker run -d --name callback-redis --network callbackapi-net --network-alias redis `
  --restart unless-stopped -p 127.0.0.1:6379:6379 `
  -v callbackapi-redis-data:/data callback-redis:local
```

Depois de PostgreSQL aceitar conexões, aplique as migrations com Tern:

```powershell
docker run --rm --network callbackapi-net --entrypoint tern `
  -e POSTGRES_HOST=postgres -e POSTGRES_PORT=5432 `
  -e POSTGRES_USER=callback -e POSTGRES_PASSWORD=callbackpass `
  -e POSTGRES_DB=callbackdb callback-api:local migrate --migrations /app/migrations
```

Inicie a API:

```powershell
docker run -d --name callback-api --network callbackapi-net `
  --restart unless-stopped -e HTTP_ADDRESS=:8080 `
  -e POSTGRES_HOST=postgres -e POSTGRES_PORT=5432 `
  -e POSTGRES_USER=callback -e POSTGRES_PASSWORD=callbackpass `
  -e POSTGRES_DB=callbackdb -e REDIS_ADDRESS=redis:6379 `
  -p 127.0.0.1:8080:8080 callback-api:local
```

A API fica disponível em `http://localhost:8080`. Verifique a API e suas
dependências em `http://localhost:8080/v1/health/ready`.

As credenciais acima são apenas para desenvolvimento local. Não as use em
outros ambientes.
