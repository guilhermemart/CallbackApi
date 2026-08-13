using System.Net;
using Xunit;

namespace CallbackApi.Tests;

public class HealthTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthTests(TestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Returns_OK()
    {
        var response = await _client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}