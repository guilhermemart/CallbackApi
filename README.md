# CallbackApi

API em Go para receber callbacks de equipamentos e sistemas, validar eventos e
disponibilizá-los para consulta e gerenciamento. O projeto também prevê uma
interface web em React conectada diretamente à API Go.

## Resultado desejado

O sistema deverá permitir que uma origem envie um evento e receba a confirmação
de que ele foi aceito. A API valida o contrato, registra o estado atual no
Redis e coloca o evento em uma fila Redis. Um worker grava o snapshot no
PostgreSQL.

Na interface React, a tela principal deverá apresentar os eventos em uma
tabela, com os mais recentes primeiro segundo o horário em que a API recebeu
cada evento. A comunicação entre o navegador e a API será direta: HTTP para
consultar e gerenciar eventos e Server-Sent Events (SSE) para receber avisos de
novos eventos.

> A tabela React, o endpoint SSE e a ordenação pelo horário de recebimento
> ainda são objetivos do projeto; não estão implementados na API atual.

## Estado atual

- API HTTP em Go com validação do contrato de eventos.
- Redis para estado atual e filas em streams.
- Worker que persiste snapshots no PostgreSQL.
- Rotas de health/readiness, criação, consulta, atualização e exclusão de
  eventos.
- Proteção contra duplicidade na criação: `event_unique_hash` é único por
  `source_id`.
- Migrations SQL versionadas executadas com Tern. A migration inicial está em
  [`migrations/001_initial_schema.sql`](migrations/001_initial_schema.sql); a
  coluna de hash e seu índice inicial estão em
  [`migrations/002_event_deduplication.sql`](migrations/002_event_deduplication.sql),
  e a unicidade permanente por emissor está em
  [`migrations/003_unique_event_hash_per_source.sql`](migrations/003_unique_event_hash_per_source.sql).
- Imagens e manifests Docker/Kubernetes para executar os serviços.

Atualmente, a rota de listagem retorna até 100 eventos ativos do Redis e usa o
`created_at` informado no payload para ordená-los. Para atender ao resultado
desejado, a API precisará registrar um horário de recebimento gerado no servidor
e usá-lo na ordenação da tabela.

## Contrato do evento

O endpoint de criação recebe `event_type`, `event_unique_hash` e `payload`. O hash
é uma string opaca gerada pelo emissor. O payload deve ser um objeto JSON com
`source` (array não vazio), `source_id` (string não vazia),
`data` (objeto), `created_at` (data e hora RFC 3339) e `created_by` (string não
vazia).

```json
{
  "event_type": "motion_detected",
  "event_unique_hash": "sender-generated-value-0001",
  "payload": {
    "source": ["camera-01"],
    "source_id": "front-door",
    "created_at": "2026-10-05T12:00:00Z",
    "created_by": "system-a",
    "data": {
      "message": "motion detected"
    }
  }
}
```

Antes de acessar PostgreSQL, a API consulta no Redis os hashes dos dez eventos
mais recentemente aceitos para o mesmo `source_id`. Uma correspondência retorna
`409 Conflict`. Se não houver correspondência, PostgreSQL impõe unicidade
permanente ao par (`source_id`, `event_unique_hash`), cobrindo hashes fora dessa
janela e requisições concorrentes. Depois de reservar o evento no banco e
enfileirá-lo no Redis, a API atualiza a lista dos dez hashes recentes e responde
`202 Accepted`. O `created_at` continua sendo usado para ordenar os eventos; ele
não define a janela de deduplicação. O worker continua processando o stream de
eventos e operações.

Antes de aplicar a migration 003 em um banco que já recebeu hashes, confira se
há pares duplicados; a criação do índice único falha enquanto eles existirem:

```sql
SELECT source_id, event_unique_hash, count(*)
FROM events
WHERE source_id IS NOT NULL AND event_unique_hash IS NOT NULL
GROUP BY source_id, event_unique_hash
HAVING count(*) > 1;
```

## Arquitetura

```text
Origem -- HTTP: callback --> API Go
                              ^   |
             HTTP: consultas |   | SSE: novos eventos
                              |   v
                       Interface React
                              |
                       tabela principal

API Go --> Redis: estado e fila --> Worker --> PostgreSQL
```

O PostgreSQL guarda os snapshots persistidos. Redis mantém o estado consultado
pela API e os streams usados pelo worker. O Tern aplica as alterações de schema
definidas em arquivos SQL versionados.

## Rotas disponíveis

| Método | Rota | Descrição |
| --- | --- | --- |
| `GET` | `/v1/health` | Verifica se a API está respondendo. |
| `GET` | `/v1/health/ready` | Verifica as conexões com PostgreSQL e Redis. |
| `GET` | `/swagger/` | Abre a interface interativa Swagger UI. |
| `GET` | `/openapi.yaml` | Retorna a especificação OpenAPI. |
| `POST` | `/v1/event/save` | Valida e enfileira um evento. |
| `GET` | `/v1/events/list` | Lista até 100 eventos ativos. |
| `GET` | `/v1/event/get/{id}` | Consulta um evento pelo ID. |
| `POST` | `/v1/event/update/{id}` | Atualiza um evento existente. |
| `PATCH` | `/v1/events/softdelete/{id}` | Marca um evento como excluído. |
| `DELETE` | `/v1/events/delete/{id}` | Enfileira a exclusão definitiva. |

## Executar localmente

O guia de execução com Docker, PostgreSQL e Redis está em
[`documents/redis-postgres-snapshot.md`](documents/redis-postgres-snapshot.md).
Para iniciar em Kubernetes com Kind, consulte [`k8s/README.md`](k8s/README.md).

Por padrão, a API escuta em `:8080`. As variáveis principais são:

Abra `http://localhost:8080/swagger/` para explorar e chamar as rotas pela
interface Swagger UI. A página carrega os arquivos da interface pelo CDN do
unpkg; a especificação OpenAPI é servida pela própria API em `/openapi.yaml`.

| Variável | Uso |
| --- | --- |
| `HTTP_ADDRESS` | Endereço HTTP da API; padrão `:8080`. |
| `POSTGRES_HOST` | Host do PostgreSQL; padrão `postgres`. |
| `POSTGRES_PORT` | Porta do PostgreSQL; padrão `5432`. |
| `POSTGRES_USER` | Usuário do PostgreSQL; padrão `postgres`. |
| `POSTGRES_PASSWORD` | Senha do PostgreSQL. |
| `POSTGRES_DB` | Banco PostgreSQL; padrão `postgres`. |
| `REDIS_ADDRESS` | Endereço Redis; padrão `redis:6379`. |
| `REDIS_PASSWORD` | Senha do Redis, se configurada. |

## Próximas etapas recomendadas

- Criar a interface React e a tabela de eventos da tela principal.
- Adicionar SSE na API Go e consumir o stream no React.
- Registrar o horário de recebimento no servidor e ordenar por ele, do mais
  recente para o mais antigo.
- Definir autenticação das origens, paginação e monitoramento do processamento.
