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
- Migrations SQL versionadas executadas com Tern. A migration inicial está em
  [`migrations/001_initial_schema.sql`](migrations/001_initial_schema.sql).
- Imagens e manifests Docker/Kubernetes para executar os serviços.

Atualmente, a rota de listagem retorna até 100 eventos ativos do Redis e usa o
`created_at` informado no payload para ordená-los. Para atender ao resultado
desejado, a API precisará registrar um horário de recebimento gerado no servidor
e usá-lo na ordenação da tabela.

## Contrato do evento

O endpoint de criação recebe `event_type` e `payload`. O payload deve ser um
objeto JSON com `source` (array não vazio), `source_id` (string não vazia),
`data` (objeto), `created_at` (data e hora RFC 3339) e `created_by` (string não
vazia).

```json
{
  "event_type": "motion_detected",
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

O endpoint de criação retorna `202 Accepted` depois de enfileirar o evento no
Redis. A gravação no PostgreSQL acontece de forma assíncrona.

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
- Definir autenticação das origens, proteção contra eventos duplicados,
  paginação e monitoramento do processamento.
