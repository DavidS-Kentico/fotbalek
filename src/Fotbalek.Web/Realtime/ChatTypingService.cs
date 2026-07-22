using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Features.Memberships;
using Fotbalek.Web.Services;

namespace Fotbalek.Web.Realtime;

/// <summary>
/// Typing indicators stay Web-only (ephemeral, no DB, no business logic — §4.4), but
/// authorization is never assumed from the circuit: membership is still verified per signal
/// via a dispatched query, exactly as the old ChatService did.
/// </summary>
public class ChatTypingService(IScopedSender sender, ChatNotifier notifier, CurrentUserAccessor currentUser)
{
    public async Task SetTypingAsync(int teamId, bool isTyping)
    {
        var userId = await currentUser.GetUserIdAsync();
        if (userId is not int uid)
            return;
        var isMember = await sender.Send(new IsTeamMemberQuery(teamId));
        if (isMember.IsFailure || !isMember.Value)
            return;
        notifier.SetTyping(teamId, uid, isTyping);
    }
}
