/**
 * ARQUIVO: Program.cs
 * UTILIDADE: Configura o servidor web, registra serviços (Injeção de Dependência) 
 * e define o pipeline de como as requisições HTTP são processadas.
 */
using CallbackApi.Features.Events;
using CallbackApi.Infrastructure.Persistence;
using CallbackApi.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

// O ponto de entrada da aplicação, onde configuramos o servidor e os serviços.
var builder = WebApplication.CreateBuilder(args);

// Adiciona o suporte para Controllers (peças que lidam com as rotas/URLs).
builder.Services.AddControllers();

// Dependency Injection (DI): Registra o <View>Service como um Singleton (uma única instância para toda a app).
builder.Services.AddScoped<EventService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Connection string 'Redis' is required.")));
builder.Services.AddSingleton<RedisEventStream>();
builder.Services.AddDbContext<CallbackDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Connection string 'Postgres' is required.")));
builder.Services.AddHostedService<PostgresSnapshotWorker>();

// Configuração do Swagger para gerar documentação automática da API.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Constrói a aplicação com as configurações acima.
var app = builder.Build();

// Configura o "Pipeline de Requisição" (Middleware).
if (app.Environment.IsDevelopment())
{
    // Ativa a interface visual do Swagger apenas em desenvolvimento.
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Redirect("/v1/health"));

app.UseExceptionHandler("/error");
// Mapeia os Controllers para que o ASP.NET saiba quais classes usar para cada rota.
app.MapControllers();

// Inicia o servidor e começa a ouvir requisições.
app.Run();

public partial class Program { }
