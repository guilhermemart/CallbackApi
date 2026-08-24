using System.Net;
using System.Text;
using System.Text.Json;
using CallbackApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CallbackApi.Tests.Features.Events.Tests;

public class EventLifecycleTests(EventIntegrationFixture fixture) : IClassFixture<EventIntegrationFixture>, IAsyncLifetime
{
    private readonly EventApiFactory _factory = new(fixture);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CallbackDbContext>();
        await database.Database.MigrateAsync();
        await database.Database.ExecuteSqlRawAsync("TRUNCATE TABLE events");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EventLifecycle_Save_Update_SoftDelete_Restore_And_Delete()
    {
        var saveResponse = await _client.PostAsync("/v1/event/save", CreateEventContent("created"));
        Assert.Equal(HttpStatusCode.Accepted, saveResponse.StatusCode);

        var eventId = await ReadIdAsync(saveResponse);
        var getResponse = await _client.GetAsync($"/v1/event/get/{eventId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        await AssertEventuallyAsync(async () =>
        {
            var persistedEvent = await GetPersistedEventAsync(eventId);
            return persistedEvent?.EventType == "created" && persistedEvent.DeletedAt is null;
        });

        var updateResponse = await _client.PostAsync($"/v1/event/update/{eventId}", CreateEventContent("updated"));
        Assert.Equal(HttpStatusCode.Accepted, updateResponse.StatusCode);

        await AssertEventuallyAsync(async () =>
            (await GetPersistedEventAsync(eventId))?.EventType == "updated");

        var softDeleteResponse = await _client.PatchAsync($"/v1/events/softdelete/{eventId}", null);
        Assert.Equal(HttpStatusCode.Accepted, softDeleteResponse.StatusCode);

        var listAfterSoftDelete = await _client.GetAsync("/v1/events/list");
        var listContent = await listAfterSoftDelete.Content.ReadAsStringAsync();
        Assert.DoesNotContain(eventId.ToString(), listContent);

        await AssertEventuallyAsync(async () =>
        {
            var persistedEvent = await GetPersistedEventAsync(eventId);
            return persistedEvent?.DeletedAt is not null
                && GetPayloadProperty(persistedEvent.Payload, "deleted_by") == "system_action";
        });

        var restoreResponse = await _client.PostAsync($"/v1/event/update/{eventId}", CreateEventContent("restored"));
        Assert.Equal(HttpStatusCode.Accepted, restoreResponse.StatusCode);

        await AssertEventuallyAsync(async () =>
        {
            var persistedEvent = await GetPersistedEventAsync(eventId);
            return persistedEvent?.EventType == "restored"
                && persistedEvent.DeletedAt is null
                && GetPayloadProperty(persistedEvent.Payload, "deleted_at") is null
                && GetPayloadProperty(persistedEvent.Payload, "deleted_by") is null;
        });

        var deleteResponse = await _client.DeleteAsync($"/v1/events/delete/{eventId}");
        Assert.Equal(HttpStatusCode.Accepted, deleteResponse.StatusCode);

        var getAfterDelete = await _client.GetAsync($"/v1/event/get/{eventId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        await AssertEventuallyAsync(async () => await GetPersistedEventAsync(eventId) is null);
    }

    [Fact]
    public async Task Readiness_Returns_OK_When_Postgres_Is_Available()
    {
        var response = await _client.GetAsync("/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", document.RootElement.GetProperty("database").GetString());
        Assert.Equal("ok", document.RootElement.GetProperty("redis").GetString());
    }

    private static StringContent CreateEventContent(string eventType)
    {
        var content = $$"""
        {
          "event_type": "{{eventType}}",
          "payload": {
            "source": ["xunit"],
            "source_id": "xunit-event",
            "data": {
              "message": "{{eventType}} event"
            },
            "created_at": "2026-08-21T12:00:00Z",
            "created_by": "xunit"
          }
        }
        """;

        return new StringContent(content, Encoding.UTF8, "application/json");
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<PersistedEvent?> GetPersistedEventAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT event_type, deleted_at, payload FROM events WHERE \"Id\" = @id",
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PersistedEvent(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.GetString(2));
    }

    private static string? GetPayloadProperty(string payload, string propertyName)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static async Task AssertEventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.Fail("The asynchronous operation did not complete within 15 seconds.");
    }

    private sealed record PersistedEvent(string EventType, DateTime? DeletedAt, string Payload);
}
