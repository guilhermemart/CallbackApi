# Arquitetura do Projeto - CallbackApi

Este documento descreve a organização e os padrões arquiteturais utilizados no projeto **CallbackApi**, uma API construída com **ASP.NET Core** (.NET 8/9+) para processamento e registro de logs via callbacks HTTP.

## 🏗️ Estrutura de Pastas

O projeto adota uma estrutura modular e organizada:

- **`Controllers/`**: Porta de entrada para as requisições HTTP. Define os endpoints da API.
  - `LogController.cs`: Gerencia o recebimento de logs via `POST`.
  - `HealthController.cs`: Monitoramento básico do status da aplicação.
- **`Services/`**: Camada de lógica de negócio. Isola as regras de processamento das rotas HTTP.
  - `LogService.cs`: Processa e registra os logs utilizando *Structured Logging*.
- **`Models/`**: Definições de estruturas de dados (DTOs - Data Transfer Objects).
  - `LogInput.cs`: Define o contrato (JSON) esperado pela API.
- **`Middleware/`**: Componentes que interceptam o fluxo de requisição/resposta.
  - `TimmingMiddleware.cs`: Mede o tempo de processamento de cada requisição.

## 🔄 Fluxo de Requisição

Quando uma requisição atinge a API, ela percorre o seguinte caminho:

1. **Middleware (`TimingMiddleware`)**: Inicia o cronômetro antes do processamento.
2. **Controller (`LogController`)**: Valida o modelo de entrada (`LogInput`) e decide qual serviço acionar.
3. **Service (`LogService`)**: Executa a lógica de persistência ou registro (neste caso, enviando para o sistema de logs do ASP.NET).
4. **Retorno**: A resposta volta pelo Pipeline, o Middleware para o cronômetro e registra a duração total antes de devolver a resposta ao cliente.

## 🛠️ Tecnologias e Padrões

- **Injeção de Dependência (DI)**: Utilizada para gerenciar o ciclo de vida dos serviços (ex: `LogService` registrado como Singleton em `Program.cs`).
- **Structured Logging**: Facilitada pelo `ILogger`, permitindo buscas e filtragens eficientes nos logs.
- **Swagger/OpenAPI**: Geração automática de documentação da API em ambiente de desenvolvimento.
- **Clean Architecture Principles**: Separação clara de responsabilidades entre transporte (Controllers), lógica (Services) e dados (Models).

## 🚀 Como Funciona a Inicialização (`Program.cs`)

O arquivo `Program.cs` configura o servidor web, registra as dependências necessárias e define a ordem dos componentes no pipeline de requisição (Middlewares).
