# Resumo do Projeto CallbackApi

## Objetivo

O projeto é uma API intermediária para receber eventos de sistemas de
segurança/CFTV, processá-los e disponibilizá-los para consulta em uma
interface web. A integração real com equipamentos Intelbras, a persistência
em banco de dados e o frontend ainda não estão implementados.

Além do objetivo funcional, o projeto serve para praticar desenvolvimento de
APIs backend com ASP.NET Core: organização por responsabilidades, validação de
entrada, injeção de dependência, logging e testes automatizados.

## Tecnologia e estrutura

A implementação atual usa **C# com ASP.NET Core**, direcionada ao **.NET 10**.
Não utiliza Python, FastAPI ou Pydantic.

```text
CallbackApi/
├── CallbackApi.slnx
├── src/
│   └── CallbackApi/
│       ├── Controllers/
│       ├── Middleware/
│       ├── Models/
│       ├── Services/
│       ├── Program.cs
│       └── CallbackApi.csproj
├── tests/
│   └── CallbackApi.Tests/
└── documents/
```

A solução inclui a API e o projeto de testes.

## Endpoints existentes

| Método | Rota | Comportamento atual |
| --- | --- | --- |
| `GET` | `/v1/health` | Retorna `200 OK` com o estado `ok` e o nome do serviço. |
| `POST` | `/v1/log` | Recebe um log, registra a informação e retorna `201 Created`. |
| Vários | `/error` | Retorna uma resposta `500` no formato Problem Details para exceções não tratadas. |

A rota `GET /` redireciona para `/v1/log`. Como `/v1/log` aceita somente
`POST`, esse redirecionamento não é adequado para uma requisição feita pelo
navegador.

## Entrada de logs

O endpoint `POST /v1/log` recebe um JSON que é desserializado no modelo
`LogInput`:

```json
{
  "log": "Erro ao conectar ao banco de dados",
  "timestamp": "2024-01-15T10:30:00Z",
  "source": "database_service"
}
```

Os campos `log` e `source` são obrigatórios. `timestamp` é opcional; quando
não é informado, a API utiliza o horário UTC atual. O endpoint gera um
identificador (`Guid`) para a resposta e retorna os dados processados.

## Processamento e logging

O `LogController` delega o processamento ao `LogService`, registrado como
singleton por injeção de dependência. Atualmente, o serviço registra uma
mensagem estruturada por meio de `ILogger<LogService>` contendo origem,
horário e texto do log.

Não há configuração explícita de gravação em arquivo, rotação de arquivos de
log ou persistência de eventos no código atual.

## Fluxo pretendido

```text
Intelbras / sistema CFTV
          |
          | callback / evento
          v
    ASP.NET Core API
          |
          | valida evento
          v
     processa evento
          |
          v
    Banco de dados
          |
          v
 Frontend React ou Vue
```

O frontend deverá consultar os eventos por endpoints da API. A atualização em
tempo real poderá ser adicionada posteriormente por SignalR ou outro mecanismo
compatível.

## Pipeline HTTP

O arquivo `Program.cs` configura:

1. controllers;
2. injeção de dependência para `LogService`;
3. Swagger/OpenAPI;
4. middleware global de exceções em `/error`;
5. mapeamento dos controllers.

Swagger é habilitado somente no ambiente de desenvolvimento.

## Testes existentes

O projeto `tests/CallbackApi.Tests` usa xUnit e
`Microsoft.AspNetCore.Mvc.Testing`. Há um teste de integração que confirma que
`GET /v1/health` retorna `200 OK`.

## Próximas evoluções

1. Definir os contratos reais de callback da Intelbras ou do sistema CFTV de
   origem.
2. Criar modelos específicos para os eventos recebidos e normalizá-los para um
   formato interno.
3. Implementar a persistência dos eventos em banco de dados.
4. Adicionar autenticação ou validação de assinatura para callbacks, conforme
   os mecanismos oferecidos pelo sistema de origem.
5. Corrigir o redirecionamento de `GET /` para uma rota que aceite `GET`, como
   `/v1/health`.
6. Criar endpoints para listar e consultar os eventos persistidos.
7. Criar um frontend em React ou Vue para visualizar os eventos.
8. Criar testes para `POST /v1/log`, validação de payloads e persistência.
9. Definir idempotência e estratégia de reprocessamento para callbacks
   duplicados ou que falhem.
