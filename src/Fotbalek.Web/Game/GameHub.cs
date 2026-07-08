using Fotbalek.Web.Game.Core;
using Fotbalek.Web.Services;
using Microsoft.AspNetCore.Authorization;
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
    PlayerService players) : Hub
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
            Context.Items[RoomKey] = room;
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
