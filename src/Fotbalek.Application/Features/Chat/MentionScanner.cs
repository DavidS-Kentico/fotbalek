namespace Fotbalek.Application.Features.Chat;

/// <summary>One roster entry the scanner can match against.</summary>
public readonly record struct RosterName(int PlayerId, string Name);

/// <summary>Where a mention sits in the body, and who it names. <see cref="Length"/> covers the '@'.</summary>
public readonly record struct MentionSpan(int Start, int Length, int PlayerId);

/// <summary>
/// The mention-matching rule, in one place. It used to live only in Web (ChatMessageView's segment
/// builder), and the server has to match too in order to write the ChatMention notifications — two
/// copies would drift and produce a highlighted mention pill with no notification, or worse the
/// reverse (AI/notifications.md §5.2).
/// <para>
/// Returning SPANS rather than markup keeps the split clean: Application owns the matching rule, Web
/// owns the rendering.
/// </para>
/// <para>
/// Three details are inherited from the original deliberately, not by accident:
/// <list type="bullet">
/// <item>longest roster name first, so "@Jan Novák" beats "@Jan" when both are on the team;</item>
/// <item>case-insensitive, and names containing spaces are accepted (chat.md §4.4);</item>
/// <item><b>no word-boundary requirement before the '@'</b> — <c>mail.foo@Alice</c> renders a mention
/// pill today and must keep matching the same way. Tightening that is a separate decision to make in
/// both places at once, not a side effect of the move.</item>
/// </list>
/// The name → player id mapping is well-defined because PlayerRules enforces case-insensitive name
/// uniqueness per team.
/// </para>
/// </summary>
public static class MentionScanner
{
    public static IReadOnlyList<MentionSpan> Scan(string body, IReadOnlyList<RosterName> roster)
    {
        if (string.IsNullOrEmpty(body) || roster.Count == 0)
            return [];

        // Sorted here rather than trusted from the caller: longest-match is part of the rule, and a
        // caller that forgot to sort would silently match the shorter name.
        var byLengthDesc = roster.OrderByDescending(r => r.Name.Length).ToList();

        var spans = new List<MentionSpan>();
        var i = 0;
        while (i < body.Length && (i = body.IndexOf('@', i)) >= 0)
        {
            RosterName? matched = null;
            foreach (var candidate in byLengthDesc)
            {
                if (candidate.Name.Length > 0 &&
                    i + 1 + candidate.Name.Length <= body.Length &&
                    string.Compare(body, i + 1, candidate.Name, 0, candidate.Name.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    matched = candidate;
                    break;
                }
            }

            if (matched is not { } hit)
            {
                i++;
                continue;
            }

            spans.Add(new MentionSpan(i, 1 + hit.Name.Length, hit.PlayerId));
            i += 1 + hit.Name.Length;
        }

        return spans;
    }
}
