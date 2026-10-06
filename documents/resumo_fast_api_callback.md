# Resumo do projeto CallbackApi

## Objetivo

CallbackApi recebe callbacks de equipamentos ou sistemas, valida um contrato
JSON genérico e disponibiliza eventos para consulta.

## Implementação

- `main.go` inicia o servidor HTTP, registra as rotas e expõe verificações de
  saúde e prontidão.
- `event.go` valida entradas, mantém o estado atual no Redis e executa o worker
  que grava snapshots no PostgreSQL.
- A CLI do Tern aplica as migrations SQL versionadas em `migrations/`.

## Fluxo de eventos

1. A API valida `event_type` e `payload`.
2. O evento é gravado no estado e no stream do Redis.
3. Um worker consome o stream e aplica o snapshot ao PostgreSQL.

O endpoint de criação retorna `202 Accepted` depois de enfileirar o evento no
Redis. A gravação no PostgreSQL ocorre de forma assíncrona.

## Contrato de entrada

```json
{
  "event_type": "motion_detected",
  "payload": {
    "source": ["equipment-x"],
    "source_id": "equipment-x-01",
    "created_at": "2026-08-13T12:00:00Z",
    "created_by": "user-id",
    "data": {
      "message": "motion detected"
    }
  }
}
```

`event_type` deve ser uma string não vazia. `payload` deve ser um objeto JSON
com `source` como array não vazio, `source_id` e `created_by` como strings não
vazias, `data` como objeto JSON e `created_at` como data e hora RFC 3339.

## Endpoints

| Método | Rota | Comportamento |
| --- | --- | --- |
| `GET` | `/v1/health` | Retorna o estado da API. |
| `GET` | `/v1/health/ready` | Verifica as conexões com PostgreSQL e Redis. |
| `POST` | `/v1/event/save` | Valida e enfileira um evento. |
| `POST` | `/v1/event/update/{id}` | Atualiza um evento existente. |
| `GET` | `/v1/event/get/{id}` | Consulta um evento por ID. |
| `GET` | `/v1/events/list` | Lista até 100 eventos ativos. |
| `PATCH` | `/v1/events/softdelete/{id}` | Marca um evento como excluído. |
| `DELETE` | `/v1/events/delete/{id}` | Enfileira a remoção definitiva. |

## Execução

Veja [`architecture.md`](architecture.md) para as variáveis de ambiente e
[`redis-postgres-snapshot.md`](redis-postgres-snapshot.md) para iniciar a API
com PostgreSQL e Redis em Docker.
