using Microsoft.AspNetCore.SignalR;

namespace Fotbalek.Web.Game;

/// <summary>
/// Singleton registry of live game rooms, keyed by <c>RoomId</c> plus a TeamId → RoomId index
/// enforcing one room per team — a v1 policy, not a structural limit (§4.2, §7). Raises
/// <see cref="Changed"/> for lobby UI, mirroring <c>PresenceTracker</c>.
/// </summary>
public sealed class GameRoomManager(IHubContext<GameHub> hubContext, ILogger<GameRoomManager> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GameRoom> _rooms = [];
    private readonly Dictionary<int, Guid> _roomByTeam = [];

    public event Action? Changed;

    public GameRoom GetOrCreateForTeam(int teamId)
    {
        GameRoom room;
        lock (_gate)
        {
            if (_roomByTeam.TryGetValue(teamId, out var existingId)
                && _rooms.TryGetValue(existingId, out var existing))
                return existing;

            room = new GameRoom(teamId, this, hubContext, logger);
            _rooms[room.RoomId] = room;
            _roomByTeam[teamId] = room.RoomId;
            room.Start();
        }
        NotifyChanged();
        return room;
    }

    public GameRoom? Get(Guid roomId)
    {
        lock (_gate)
        {
            return _rooms.GetValueOrDefault(roomId);
        }
    }

    public GameRoom? GetForTeam(int teamId)
    {
        lock (_gate)
        {
            return _roomByTeam.TryGetValue(teamId, out var roomId)
                ? _rooms.GetValueOrDefault(roomId)
                : null;
        }
    }

    /// <summary>Removes and shuts down a room (End game, idle cleanup). Idempotent.</summary>
    public void Destroy(Guid roomId)
    {
        GameRoom? room;
        lock (_gate)
        {
            if (!_rooms.Remove(roomId, out room))
                return;
            _roomByTeam.Remove(room.TeamId);
        }
        _ = room.ShutdownAsync(); // final closed broadcast; fire-and-forget
        NotifyChanged();
    }

    internal void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GameRoomManager.Changed subscriber threw");
        }
    }
}
