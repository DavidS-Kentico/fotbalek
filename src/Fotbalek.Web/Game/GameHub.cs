using Fotbalek.Web.Game.Core;
using Fotbalek.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;

namespace Fotbalek.Web.Game;

/// <summary>
/// Connection lifecycle + input only (§4.2) — a logic-free adapter over <see cref="GameRoom"/>.
/// All other lobby mutations go through the room API directly from the Blazor circuit.
/// The default authorization policy (authenticated + NameIdentifier claim) applies, so
/// admin-only cookie sessions are rejected for free.
/// </summary>
[Authorize]
public class GameHub(
    GameRoomManager rooms,
    TeamMembershipService membership,
    PlayerService players,
    ILogger<GameHub> logger) : Hub
{
    private const string RoomKey = "room";

    public async Task<JoinRoomResult?> JoinRoom(Guid roomId)
    {
        // One room per connection: OnDisconnectedAsync only reaches the room in Items, so a
        // second join would leave the first room counting this connection as live forever
        // (seat never grace-freed, idle cleanup blocked). The real client never re-joins on a
        // live connection — a reconnect is a brand-new connection with fresh Items.
        if (Context.Items.ContainsKey(RoomKey))
            return null;

        var room = rooms.Get(roomId);
        if (room == null || !int.TryParse(Context.UserIdentifier, out var userId))
            return null;
        if (!await membership.IsMemberAsync(userId, room.TeamId))
            return null;

        // Display name/avatar: the user's claimed Player in this team; the generic fallback is
        // defensive — TeamLayout redirects unclaimed members before they reach team pages (§3.7).
        var player = await players.GetUserPlayerInTeamAsync(room.TeamId, userId);
        var name = player?.Name ?? Context.User?.Identity?.Name ?? "Player";
        var avatarId = player?.AvatarId ?? 1;

        await Groups.AddToGroupAsync(Context.ConnectionId, room.GroupName);
        var result = room.Connect(userId, Context.ConnectionId, name, avatarId);
        if (result != null)
        {
            Context.Items[RoomKey] = room;
            // Record the negotiated transport once per connection (§12). A silent fallback to SSE /
            // long polling (proxy / App Service misconfig) would wreck latency — this makes it visible
            // instead of showing up as unexplained RTT.
            var transport = Context.Features.Get<IHttpTransportFeature>()?.TransportType.ToString() ?? "unknown";
            GameTelemetry.Transport(logger, room.TeamId, userId, transport);
        }
        return result;
    }

    /// <summary>Held-key state change: <paramref name="hand"/> 0 = left (W/S), 1 = right (arrows);
    /// <paramref name="dir"/> -1/0/+1. The server maps hand → rod(s) from the sender's seat, so
    /// ownership can't be spoofed (§2.2).</summary>
    public void HandInput(int hand, int dir)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value)
            && value is GameRoom room
            && int.TryParse(Context.UserIdentifier, out var userId))
        {
            room.SetHandInput(userId, hand, dir);
        }
    }

    /// <summary>Catch-key state change: <paramref name="held"/> true while a catch key (either Shift)
    /// is down. Arms all the caller's rods so the ball traps on whichever of their figures it reaches;
    /// release fires it. Hand-agnostic — one flag, resolved server-side from the seat.</summary>
    public void Catch(bool held)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value)
            && value is GameRoom room
            && int.TryParse(Context.UserIdentifier, out var userId))
        {
            room.SetCatch(userId, held);
        }
    }

    /// <summary>Lift-key state change (§skill-lift): <paramref name="hand"/> 0 = left (A/D), 1 = right
    /// (←/→); <paramref name="held"/> true while that hand's lift key is down. The server maps hand → rod(s)
    /// from the seat, so you can only lift your own rods; it's a no-op unless the room enables rod-lift.</summary>
    public void Lift(int hand, bool held)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value)
            && value is GameRoom room
            && int.TryParse(Context.UserIdentifier, out var userId))
        {
            room.SetLift(userId, hand, held);
        }
    }

    /// <summary>SPACE state change: <paramref name="held"/> true while SPACE is down. Held per user to
    /// charge the goalie's shot; its edges drive the trapped-ball action (outfield pass on press, goalie
    /// launch on release). The server resolves the trapped rod and validates the caller drives it, so it
    /// can't be spoofed.</summary>
    public void Space(bool held)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value)
            && value is GameRoom room
            && int.TryParse(Context.UserIdentifier, out var userId))
        {
            room.SetSpace(userId, held);
        }
    }

    /// <summary>No-op round-trip probe used only for client RTT measurement (§12). The client times the
    /// invoke; the server does nothing, so this measures the real application-level latency the game
    /// experiences through SignalR with negligible added work.</summary>
    public void Ping()
    {
    }

    /// <summary>Receives a client's windowed latency summary (RTT + snapshot inter-arrival) while playing
    /// and records it as server-side telemetry (§12). Logic-free adapter; ignored outside a room.</summary>
    public void ReportStats(ClientStatsDto stats)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value)
            && value is GameRoom room
            && int.TryParse(Context.UserIdentifier, out var userId))
        {
            room.RecordClientStats(userId, stats);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var value) && value is GameRoom room)
        {
            // connection.stop() (page teardown) yields a null exception → graceful leave;
            // network loss / ping timeout yield non-null → 30 s grace hold (§3.5).
            room.Disconnect(Context.ConnectionId, graceful: exception == null);
        }
        return base.OnDisconnectedAsync(exception);
    }
}
