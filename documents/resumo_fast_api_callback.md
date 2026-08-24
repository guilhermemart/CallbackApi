# Resumo do Projeto CallbackApi

## Objetivo

O CallbackApi recebe callbacks/eventos de equipamentos ou sistemas CFTV, valida um contrato genérico e disponibiliza os eventos para consulta futura por um frontend. A API foi implementada em C# com ASP.NET Core; não utiliza FastAPI ou Python.

O contrato é genérico porque há múltiplas origens de eventos. O conteúdo específico de cada fabricante é preservado no campo `payload`.

## Estrutura atual

```text
CallbackApi/
├── CallbackApi.slnx
├── src/CallbackApi/
│   ├── Features/
│   │   ├── Errors/
│   │   ├── Events/
│   │   │   └── Models/
│   │   ├── Health/
│   ├── Infrastructure/
│   │   ├── Persistence/
│   │   └── Redis/
│   ├── Program.cs
│   └── CallbackApi.csproj
├── tests/CallbackApi.Tests/
├── docker/
│   ├── postgresql/
│   └── redis/
└── documents/
```

`Features` agrupa cada domínio da API. `Infrastructure` concentra as integrações com Redis e PostgreSQL.

## Fluxo atual de eventos

```text
Intelbras / sistema CFTV
          |
          | callback / evento
          v
     ASP.NET Core API
          |
          | valida EventInput
          v
     EventService
          |
          +--> Redis: estado atual do evento
          |
          +--> Redis Stream: fila de persistência
                                  |
                                  v
                         PostgresSnapshotWorker
                                  |
                                  v
                             PostgreSQL
          |
          v
    GET /v1/events/list
          |
          v
   Frontend futuro (React ou Vue)
```

O Redis possui dois papéis:

- `callback:events` é o Stream usado como fila para o worker de snapshot.
- `callback:event-operations` é o Stream com operações assíncronas de exclusão.
- `callback:event:{id}` mantém o estado atual e consultável de cada evento.
- `callback:events:index` mantém os IDs ordenados pelo horário de recebimento, usados pela listagem.

## Contrato de entrada

O endpoint de inclusão recebe um `EventInput`:

```json
{
  "event_type": "door_opened",
  "payload": {
    "source": ["camera-01"],
    "source_id": "front-door",
    "data": {
      "message": "door opened"
    }
  }
}
```

`payload` é um objeto JSON. No modelo C#, ele é preservado como texto JSON para ser armazenado no PostgreSQL como `jsonb` e no Redis sem depender do formato de um fabricante.

Cada evento também possui os campos internos `id`, `created_at`, `updated_at` e `deleted_at`.

## Endpoints existentes

| Método | Rota | Comportamento |
| --- | --- | --- |
| `GET` | `/v1/health` | Retorna o estado da API. |
| `GET` | `/v1/health/ready` | Executa `SELECT 1` no PostgreSQL e `PING` no Redis; retorna `200` quando ambos estão acessíveis, caso contrário retorna `503`. |
| `POST` | `/v1/event/save` | Cria um evento sem ID no corpo e enfileira a persistência. Retorna `202 Accepted`. |
| `POST` | `/v1/event/update/{id}` | Reescreve `event_type` e `payload` de um evento existente, define `deleted_at` como `null` e enfileira a atualização do PostgreSQL. Retorna `202 Accepted`. |
| `GET` | `/v1/event/get/{id}` | Retorna o evento do Redis; se não estiver no Redis, consulta PostgreSQL, preenche o Redis e retorna o registro. |
| `GET` | `/v1/events/list` | Retorna até 100 eventos ativos a partir do estado no Redis. |
| `PATCH` | `/v1/events/softdelete/{id}` | Atualiza o estado no Redis e enfileira a exclusão lógica. Retorna `202 Accepted`. |
| `DELETE` | `/v1/events/delete/{id}` | Remove o estado Redis e enfileira a remoção no PostgreSQL. Retorna `202 Accepted`. |
| Vários | `/error` | Retorna Problem Details para exceções não tratadas. |

## Persistência e exclusão lógica

O `PostgresSnapshotWorker` consome eventos e operações dos Redis Streams e aplica as alterações na tabela `events` do PostgreSQL. A tabela contém:

- `id`;
- `event_type`;
- `payload` (`jsonb`);
- `created_at`;
- `updated_at`;
- `deleted_at` (nulo enquanto o evento está ativo).

No soft delete, a API usa um único horário UTC atual e o valor temporário
`"system_action"` para `deleted_by` enquanto não existe autenticação, para:

1. atualizar `deleted_at` e `deleted_by` dentro do `payload` guardado no estado Redis;
2. definir o `DeletedAt` do estado Redis, para que `GET /v1/events/list` não retorne o evento;
3. publicar uma operação para o worker definir `events.deleted_at` e `events.updated_at` no PostgreSQL.

Se `payload.deleted_at` já existir, ele é substituído pelo horário do soft delete. O payload atualizado também é persistido no PostgreSQL, e a coluna `events.deleted_at` continua sendo a referência de exclusão lógica no banco.

No delete definitivo, a API remove primeiro o estado e o índice Redis e publica a operação de remoção. Quando o worker posteriormente lê a mensagem original de criação, ele consulta o estado Redis: se o estado não existir, reconhece a mensagem sem recriar o registro no PostgreSQL.

`GET /v1/event/get/{id}` consulta primeiro o Redis. Quando encontra o estado, publica uma operação de sincronização; o worker reescreve o PostgreSQL somente se `updated_at` for diferente. Quando o Redis não possui o estado, a API consulta o PostgreSQL e repopula o Redis. Um marcador Redis de delete definitivo impede que um registro ainda pendente de remoção no PostgreSQL seja repopulado durante esse intervalo. O worker remove esse marcador após concluir a operação de delete no PostgreSQL.

## Configuração da aplicação

`Program.cs` registra:

1. controllers e Swagger em desenvolvimento;
2. `EventService` como scoped;
3. a conexão Redis e `RedisEventStream`;
4. `CallbackDbContext` com o provider Npgsql;
5. `PostgresSnapshotWorker` como serviço em segundo plano;
6. middleware global de exceções em `/error`.

As connection strings de desenvolvimento para Redis e PostgreSQL ficam em `appsettings.Development.json`.

## Estado de verificação

O projeto compila e os testes atuais passam. A execução integrada requer Redis e PostgreSQL acessíveis pelas connection strings configuradas e as migrations aplicadas ao banco.

O teste `Features/Events/Tests/EventLifecycleTests.cs` cria instâncias temporárias de Redis e PostgreSQL com Testcontainers e valida o fluxo save, update, soft delete, restauração e delete definitivo.

## Próximos passos

1. Aplicar e validar as migrations do PostgreSQL no ambiente local.
2. Criar testes de integração para inclusão, listagem, soft delete e delete.
3. Definir autenticação ou assinatura de callbacks por origem.
4. Definir idempotência para callbacks duplicados.
5. Criar filtros e paginação para a listagem de eventos.
6. Criar o frontend React ou Vue para o grid de eventos.
