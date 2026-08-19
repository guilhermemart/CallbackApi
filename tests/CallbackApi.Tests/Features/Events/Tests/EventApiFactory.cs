using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CallbackApi.Tests.Features.Events.Tests;

public sealed class EventApiFactory(EventIntegrationFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", fixture.PostgresConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", fixture.RedisConnectionString);
    }
}
