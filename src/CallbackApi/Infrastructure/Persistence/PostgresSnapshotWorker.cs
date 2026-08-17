using System.Text.Json;
using CallbackApi.Features.Events.Models;
using CallbackApi.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CallbackApi.Infrastructure.Persistence;

public class PostgresSnapshotWorker(
    RedisEventStream eventStream,
    IServiceScopeFactory scopeFactory,
    ILogger<PostgresSnapshotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await eventStream.EnsureConsumerGroupAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var eventEntries = await eventStream.ReadPendingAsync();
                if (eventEntries.Length == 0)
                {
                    eventEntries = await eventStream.ReadNewAsync();
                }

                var operationEntries = await eventStream.ReadPendingOperationsAsync();
                if (operationEntries.Length == 0)
                {
                    operationEntries = await eventStream.ReadNewOperationsAsync();
                }

                foreach (var entry in eventEntries)
                {
                    await PersistAndAcknowledgeAsync(entry, stoppingToken);
                }

                foreach (var entry in operationEntries)
                {
                    await ApplyOperationAndAcknowledgeAsync(entry, stoppingToken);
                }

                if (eventEntries.Length == 0 && operationEntries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "postgres_snapshot_failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task PersistAndAcknowledgeAsync(StreamEntry entry, CancellationToken cancellationToken)
    {
        var serializedEvent = entry.Values.Single(value => value.Name == "event").Value.ToString();
        var @event = JsonSerializer.Deserialize<Event>(serializedEvent)
            ?? throw new JsonException("Redis stream event could not be deserialized.");

        // O estado Redis é a fonte imediata. Se não existir, um delete definitivo
        // foi solicitado antes deste item chegar ao PostgreSQL.
        var currentState = await eventStream.GetStateAsync(@event.Id, cancellationToken);
        if (currentState is null)
        {
            await eventStream.AcknowledgeAsync(entry.Id);
            logger.LogInformation("event_snapshot_skipped event_id={eventId}", @event.Id);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CallbackDbContext>();

        var alreadyPersisted = await database.Events
            .AsNoTracking()
            .AnyAsync(eventItem => eventItem.Id == @event.Id, cancellationToken);

        if (!alreadyPersisted)
        {
            database.Events.Add(currentState);
            await database.SaveChangesAsync(cancellationToken);
        }

        await eventStream.AcknowledgeAsync(entry.Id);
        logger.LogInformation("event_snapshotted event_id={eventId}", currentState.Id);
    }

    private async Task ApplyOperationAndAcknowledgeAsync(StreamEntry entry, CancellationToken cancellationToken)
    {
        var operation = GetEntryValue(entry, "operation");

        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CallbackDbContext>();

        if (operation == RedisEventStream.SoftDeleteOperation)
        {
            var serializedEvent = GetEntryValue(entry, "event");
            var deletedEvent = JsonSerializer.Deserialize<Event>(serializedEvent)
                ?? throw new JsonException("Redis soft-delete event could not be deserialized.");

            var persistedEvent = await database.Events.SingleOrDefaultAsync(
                eventItem => eventItem.Id == deletedEvent.Id,
                cancellationToken);

            if (persistedEvent is not null && persistedEvent.DeletedAt is null)
            {
                persistedEvent.DeletedAt = deletedEvent.DeletedAt;
                persistedEvent.UpdatedAt = deletedEvent.UpdatedAt;
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        else if (operation == RedisEventStream.UpdateOperation)
        {
            var serializedEvent = GetEntryValue(entry, "event");
            var updatedEvent = JsonSerializer.Deserialize<Event>(serializedEvent)
                ?? throw new JsonException("Redis update event could not be deserialized.");

            var persistedEvent = await database.Events.SingleOrDefaultAsync(
                eventItem => eventItem.Id == updatedEvent.Id,
                cancellationToken);

            if (persistedEvent is not null)
            {
                persistedEvent.EventType = updatedEvent.EventType;
                persistedEvent.Payload = updatedEvent.Payload;
                persistedEvent.UpdatedAt = updatedEvent.UpdatedAt;
                persistedEvent.DeletedAt = null;
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        else if (operation == RedisEventStream.SyncOperation)
        {
            var serializedEvent = GetEntryValue(entry, "event");
            var redisEvent = JsonSerializer.Deserialize<Event>(serializedEvent)
                ?? throw new JsonException("Redis sync event could not be deserialized.");

            var persistedEvent = await database.Events.SingleOrDefaultAsync(
                eventItem => eventItem.Id == redisEvent.Id,
                cancellationToken);

            if (persistedEvent is not null && persistedEvent.UpdatedAt != redisEvent.UpdatedAt)
            {
                persistedEvent.EventType = redisEvent.EventType;
                persistedEvent.Payload = redisEvent.Payload;
                persistedEvent.UpdatedAt = redisEvent.UpdatedAt;
                persistedEvent.DeletedAt = redisEvent.DeletedAt;
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        else if (operation == RedisEventStream.HardDeleteOperation)
        {
            var eventId = Guid.Parse(GetEntryValue(entry, "event_id"));
            var persistedEvent = await database.Events.SingleOrDefaultAsync(
                eventItem => eventItem.Id == eventId,
                cancellationToken);

            if (persistedEvent is not null)
            {
                database.Events.Remove(persistedEvent);
                await database.SaveChangesAsync(cancellationToken);
            }

            await eventStream.RemoveHardDeleteMarkerAsync(eventId, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException($"Unknown event operation '{operation}'.");
        }

        await eventStream.AcknowledgeOperationAsync(entry.Id);
        logger.LogInformation("event_operation_applied operation={operation}", operation);
    }

    private static string GetEntryValue(StreamEntry entry, RedisValue name)
    {
        return entry.Values.Single(value => value.Name == name).Value.ToString();
    }
}
