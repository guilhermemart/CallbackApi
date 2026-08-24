using System.Text.Json;
using CallbackApi.Features.Events.Models;
using StackExchange.Redis;

namespace CallbackApi.Infrastructure.Redis;

public class RedisEventStream(IConnectionMultiplexer connection)
{
    public const string StreamKey = "callback:events";
    public const string OperationStreamKey = "callback:event-operations";
    public const string SoftDeleteOperation = "soft_delete";
    public const string HardDeleteOperation = "hard_delete";
    public const string UpdateOperation = "update";
    public const string SyncOperation = "sync";
    private const string EventStateKeyPrefix = "callback:event:";
    private const string EventDeletedKeyPrefix = "callback:event:deleted:";
    private const string EventIndexKey = "callback:events:index";
    public const string GroupName = "postgres-snapshot";
    public const string ConsumerName = "postgres-snapshot-worker";

    private readonly IDatabase _database = connection.GetDatabase();

    public async Task EnqueueAsync(Event @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.KeyDeleteAsync(GetEventDeletedKey(@event.Id));
        await SaveStateAsync(@event, cancellationToken);

        var serializedEvent = JsonSerializer.Serialize(@event);
        await _database.StreamAddAsync(StreamKey, "event", serializedEvent);
    }

    public async Task<Event?> GetStateAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serializedEvent = await _database.StringGetAsync(GetEventStateKey(id));
        return serializedEvent.HasValue
            ? JsonSerializer.Deserialize<Event>(serializedEvent.ToString())
            : null;
    }

    public async Task SaveStateAsync(Event @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serializedEvent = JsonSerializer.Serialize(@event);
        await _database.StringSetAsync(GetEventStateKey(@event.Id), serializedEvent);
        await _database.SortedSetAddAsync(
            EventIndexKey,
            @event.Id.ToString(),
            @event.CreatedAt.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalMilliseconds);
    }

    public async Task<bool> IsHardDeletedAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.KeyExistsAsync(GetEventDeletedKey(id));
    }

    public async Task RemoveHardDeleteMarkerAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.KeyDeleteAsync(GetEventDeletedKey(id));
    }

    public async Task QueueSoftDeleteAsync(Event @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await SaveStateAsync(@event, cancellationToken);
        await _database.StreamAddAsync(
            OperationStreamKey,
            [
                new NameValueEntry("operation", SoftDeleteOperation),
                new NameValueEntry("event", JsonSerializer.Serialize(@event))
            ]);
    }

    public async Task QueueUpdateAsync(Event @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await SaveStateAsync(@event, cancellationToken);
        await _database.StreamAddAsync(
            OperationStreamKey,
            [
                new NameValueEntry("operation", UpdateOperation),
                new NameValueEntry("event", JsonSerializer.Serialize(@event))
            ]);
    }

    public async Task QueuePostgresSyncAsync(Event @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.StreamAddAsync(
            OperationStreamKey,
            [
                new NameValueEntry("operation", SyncOperation),
                new NameValueEntry("event", JsonSerializer.Serialize(@event))
            ]);
    }

    public async Task QueueHardDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.StringSetAsync(GetEventDeletedKey(id), "1");
        await RemoveStateAsync(id, cancellationToken);
        await _database.StreamAddAsync(
            OperationStreamKey,
            [
                new NameValueEntry("operation", HardDeleteOperation),
                new NameValueEntry("event_id", id.ToString())
            ]);
    }

    public async Task EnsureConsumerGroupAsync()
    {
        await EnsureConsumerGroupAsync(StreamKey);
        await EnsureConsumerGroupAsync(OperationStreamKey);
    }

    private async Task EnsureConsumerGroupAsync(RedisKey streamKey)
    {
        try
        {
            await _database.StreamCreateConsumerGroupAsync(
                streamKey,
                GroupName,
                "0-0",
                createStream: true);
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("BUSYGROUP"))
        {
            // O grupo já existe e pode continuar consumindo o stream.
        }
    }

    public Task<StreamEntry[]> ReadPendingAsync()
    {
        return _database.StreamReadGroupAsync(
            StreamKey,
            GroupName,
            ConsumerName,
            "0-0",
            count: 10);
    }

    public Task<StreamEntry[]> ReadNewAsync()
    {
        return _database.StreamReadGroupAsync(
            StreamKey,
            GroupName,
            ConsumerName,
            ">",
            count: 10);
    }

    public Task AcknowledgeAsync(RedisValue entryId)
    {
        return _database.StreamAcknowledgeAsync(StreamKey, GroupName, entryId);
    }

    public Task<StreamEntry[]> ReadPendingOperationsAsync()
    {
        return _database.StreamReadGroupAsync(
            OperationStreamKey,
            GroupName,
            ConsumerName,
            "0-0",
            count: 10);
    }

    public Task<StreamEntry[]> ReadNewOperationsAsync()
    {
        return _database.StreamReadGroupAsync(
            OperationStreamKey,
            GroupName,
            ConsumerName,
            ">",
            count: 10);
    }

    public Task AcknowledgeOperationAsync(RedisValue entryId)
    {
        return _database.StreamAcknowledgeAsync(OperationStreamKey, GroupName, entryId);
    }

    public async Task<IReadOnlyList<Event>> ReadLatestAsync(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var eventIds = await _database.SortedSetRangeByRankAsync(
            EventIndexKey,
            0,
            count - 1,
            Order.Descending);

        var events = await Task.WhenAll(eventIds.Select(id => GetStateAsync(Guid.Parse(id.ToString()), cancellationToken)));

        return events
            .OfType<Event>()
            .Where(eventItem => eventItem.DeletedAt is null)
            .ToList();
    }

    public async Task RemoveStateAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.KeyDeleteAsync(GetEventStateKey(id));
        await _database.SortedSetRemoveAsync(EventIndexKey, id.ToString());
    }

    private static RedisKey GetEventStateKey(Guid id) => $"{EventStateKeyPrefix}{id}";

    private static RedisKey GetEventDeletedKey(Guid id) => $"{EventDeletedKeyPrefix}{id}";
}
