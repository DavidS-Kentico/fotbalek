namespace Fotbalek.SharedKernel;

/// <summary>
/// What a notification is about. Lives here because both the entity and the DTO use it
/// (AI/architecture.md §7 step 2).
/// <para>
/// This enum is the ONLY thing the Application layer knows about presentation: no wording, no
/// icons and no URLs live in Application or on the entity — see Web's NotificationDisplay
/// (AI/notifications.md §3.2, §10).
/// </para>
/// </summary>
public enum NotificationType
{
    /// <summary>Someone else recorded a match you played in. Carries MatchId + ActorPlayerId.</summary>
    MatchRecorded = 1,

    /// <summary>You were @-mentioned in a team's chat. Carries ChatMessageId + ActorPlayerId.</summary>
    ChatMention = 2,

    /// <summary>Someone reacted to your chat message. Carries ChatMessageId, ActorPlayerId, Emoji.</summary>
    ChatReaction = 3,

    /// <summary>A season of your team started. Carries SeasonId.</summary>
    SeasonStarted = 4,

    /// <summary>A season closed. Carries SeasonId + Value = your final rank (null if you did not
    /// participate or were inactive at close).</summary>
    SeasonEnded = 5,

    /// <summary>You won a season award. Carries SeasonId, Category, Value = award rank (1–3) and,
    /// for a pair award, SubjectPlayerId = your partner.</summary>
    SeasonAward = 6,

    /// <summary>You took the #1 spot of a ladder. Carries Category, MatchId, SeasonId (null =
    /// all-time scope) and SubjectPlayerId = your partner for the pair ladder.</summary>
    LadderLeadTaken = 7,

    /// <summary>You lost the #1 spot of a ladder. Same payload, SubjectPlayerId = who took it.</summary>
    LadderLeadLost = 8,

    /// <summary>New personal-best all-time ELO. Carries MatchId + Value = the new peak.</summary>
    PeakElo = 9,

    /// <summary>A win-streak threshold was reached. Carries MatchId + Value = streak length.</summary>
    WinStreak = 10,

    /// <summary>A matches-played milestone was reached. Carries MatchId + Value = total matches.</summary>
    MatchMilestone = 11,

    /// <summary>You beat the opponent who beats you most. Carries MatchId + SubjectPlayerId.</summary>
    NemesisBeaten = 12,
}
