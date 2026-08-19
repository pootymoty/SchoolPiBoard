using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

namespace SchoolPiBoard.Online.Realtime;

/// <summary>Одно подключение участника. У одного человека может быть несколько вкладок.</summary>
public sealed record PresenceEntry(string ConnectionId, Guid UserId, string Name, string Color, string Role);

/// <summary>
/// Кто сейчас в доске. Состояние эфемерное: переживать перезапуск сервера
/// ему не нужно, а вот пережить его размножение на несколько инстансов —
/// обязательно, поэтому в бою это Redis.
/// </summary>
public interface IPresenceStore
{
    Task JoinAsync(Guid boardId, PresenceEntry entry);
    Task<PresenceEntry?> LeaveAsync(Guid boardId, string connectionId);
    Task<IReadOnlyList<PresenceEntry>> ListAsync(Guid boardId);
}

/// <summary>Для разработки на одном процессе.</summary>
public sealed class MemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, PresenceEntry>> _boards = new();

    public Task JoinAsync(Guid boardId, PresenceEntry entry)
    {
        _boards.GetOrAdd(boardId, _ => new ConcurrentDictionary<string, PresenceEntry>())[entry.ConnectionId] = entry;
        return Task.CompletedTask;
    }

    public Task<PresenceEntry?> LeaveAsync(Guid boardId, string connectionId)
    {
        if (_boards.TryGetValue(boardId, out var board) && board.TryRemove(connectionId, out var entry))
        {
            if (board.IsEmpty)
                _boards.TryRemove(boardId, out _);

            return Task.FromResult<PresenceEntry?>(entry);
        }

        return Task.FromResult<PresenceEntry?>(null);
    }

    public Task<IReadOnlyList<PresenceEntry>> ListAsync(Guid boardId)
        => Task.FromResult<IReadOnlyList<PresenceEntry>>(
            _boards.TryGetValue(boardId, out var board) ? board.Values.ToList() : Array.Empty<PresenceEntry>());
}

/// <summary>
/// Присутствие в Redis: хеш на доску, поле — идентификатор соединения.
/// У ключа есть срок жизни: если инстанс упадёт, не сняв участников,
/// запись исчезнет сама, а не останется «призраком» навсегда.
/// </summary>
public sealed class RedisPresenceStore : IPresenceStore
{
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(12);

    private readonly IConnectionMultiplexer _redis;

    public RedisPresenceStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private static RedisKey Key(Guid boardId) => $"spb:presence:{boardId}";

    public async Task JoinAsync(Guid boardId, PresenceEntry entry)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync(Key(boardId), entry.ConnectionId, JsonSerializer.Serialize(entry));
        await db.KeyExpireAsync(Key(boardId), KeyLifetime);
    }

    public async Task<PresenceEntry?> LeaveAsync(Guid boardId, string connectionId)
    {
        var db = _redis.GetDatabase();
        var value = await db.HashGetAsync(Key(boardId), connectionId);
        await db.HashDeleteAsync(Key(boardId), connectionId);
        return Parse(value);
    }

    public async Task<IReadOnlyList<PresenceEntry>> ListAsync(Guid boardId)
    {
        var values = await _redis.GetDatabase().HashGetAllAsync(Key(boardId));

        var result = new List<PresenceEntry>(values.Length);
        foreach (var value in values)
        {
            var entry = Parse(value.Value);
            if (entry is not null)
                result.Add(entry);
        }

        return result;
    }

    private static PresenceEntry? Parse(RedisValue value)
    {
        if (!value.HasValue)
            return null;

        try
        {
            // ToString(), а не приведение: у RedisValue много неявных
            // преобразований, и перегрузка Deserialize выбиралась бы наугад.
            return JsonSerializer.Deserialize<PresenceEntry>(value.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
