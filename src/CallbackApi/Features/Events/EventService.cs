using System.Text.Json;
using System.Text.Json.Nodes;
using CallbackApi.Features.Events.Models;
using CallbackApi.Infrastructure.Persistence;
using CallbackApi.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;

namespace CallbackApi.Features.Events;

public class EventService
{
    private const string DefaultDeletedBy = "system_action";
    private readonly RedisEventStream _eventStream;
    private readonly CallbackDbContext _database;
    private readonly ILogger<EventService> _logger;

    public EventService(
        RedisEventStream eventStream,
        CallbackDbContext database,
        ILogger<EventService> logger)
    {
        _eventStream = eventStream;
        _database = database;
        _logger = logger;
    }

    public async Task<Event> QueueAsync(EventInput input, CancellationToken cancellationToken)
    {
        return await CreateUpdateAsync(Guid.Empty, input, cancellationToken)
            ?? throw new InvalidOperationException("A new event could not be created.");
    }

    public async Task<Event?> UpdateAsync(Guid id, EventInput input, CancellationToken cancellationToken)
    {
        return await CreateUpdateAsync(id, input, cancellationToken);
    }

    private async Task<Event?> CreateUpdateAsync(Guid id, EventInput input, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            var now = DateTime.UtcNow;
            var newEvent = new Event
            {
                Id = Guid.NewGuid(),
                EventType = input.EventType,
                Payload = input.Payload.GetRawText(),
                CreatedAt = input.GetCreatedAt(),
                UpdatedAt = now,
                DeletedAt = null
            };

            await _eventStream.EnqueueAsync(newEvent, cancellationToken);

            _logger.LogInformation(
                "event_queued id={eventId} event_type={eventType} created_at={createdAt}",
                newEvent.Id,
                newEvent.EventType,
                newEvent.CreatedAt);

            return newEvent;
        }

        var redisEvent = await _eventStream.GetStateAsync(id, cancellationToken);
        if (redisEvent is null)
        {
            return null;
        }

        var payload = JsonNode.Parse(input.Payload.GetRawText()) as JsonObject;
        if (payload is null)
        {
            throw new InvalidOperationException("The event payload must be a JSON object to update an event.");
        }

        payload["deleted_at"] = null;
        payload["deleted_by"] = null;
        redisEvent.EventType = input.EventType;
        redisEvent.Payload = payload.ToJsonString();
        redisEvent.UpdatedAt = DateTime.UtcNow;
        redisEvent.DeletedAt = null;
        await _eventStream.QueueUpdateAsync(redisEvent, cancellationToken);

        return redisEvent;
    }

    public async Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken)
    {
        return await _eventStream.ReadLatestAsync(100, cancellationToken);
    }

    public async Task<Event?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var redisEvent = await _eventStream.GetStateAsync(id, cancellationToken);
        if (redisEvent is not null)
        {
            await _eventStream.QueuePostgresSyncAsync(redisEvent, cancellationToken);
            return redisEvent;
        }

        if (await _eventStream.IsHardDeletedAsync(id, cancellationToken))
        {
            return null;
        }

        var postgresEvent = await _database.Events
            .AsNoTracking()
            .SingleOrDefaultAsync(eventItem => eventItem.Id == id, cancellationToken);

        if (postgresEvent is null)
        {
            return null;
        }

        await _eventStream.SaveStateAsync(postgresEvent, cancellationToken);
        return postgresEvent;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var redisEvent = await _eventStream.GetStateAsync(id, cancellationToken);
        if (redisEvent is null || redisEvent.DeletedAt is not null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var payload = JsonNode.Parse(redisEvent.Payload) as JsonObject;

        if (payload is null)
        {
            throw new InvalidOperationException("The event payload must be a JSON object to apply soft delete.");
        }

        payload["deleted_at"] = now;
        payload["deleted_by"] = DefaultDeletedBy;

        redisEvent.Payload = payload.ToJsonString();
        redisEvent.DeletedAt = now;
        redisEvent.UpdatedAt = now;
        await _eventStream.QueueSoftDeleteAsync(redisEvent, cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var redisEvent = await _eventStream.GetStateAsync(id, cancellationToken);
        if (redisEvent is null)
        {
            return false;
        }

        await _eventStream.QueueHardDeleteAsync(id, cancellationToken);
        return true;
    }
}
