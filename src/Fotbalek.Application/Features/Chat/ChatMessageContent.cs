using System.Globalization;
using System.Text;
using Fotbalek.SharedKernel;

namespace Fotbalek.Application.Features.Chat;

/// <summary>
/// Normalizes raw chat input into a safe, storable body — the single source of truth shared by
/// send and edit so the two paths can never drift. Guards the layout-abuse vectors a plain
/// trim/clamp misses:
///   • walls of blank lines and runaway line counts — vertical-height abuse;
///   • stacked combining marks ("Zalgo") that bleed past line-height into neighbouring messages;
///   • invisible control / bidirectional-override / zero-width characters — spoofing and
///     invisible spam (which also lets an "empty" message slip past a naive Trim check).
/// The body is length-clamped as it is built. Returns false when nothing meaningful survives
/// (empty, whitespace-only, or all-invisible) — both handlers surface that as "the message is
/// empty".
/// </summary>
internal static class ChatMessageContent
{
    /// <summary>At most one visually-blank line between paragraphs.</summary>
    private const int MaxConsecutiveNewlines = 2;

    /// <summary>Hard cap on line count; further newlines fold into a space so height stays bounded.</summary>
    private const int MaxLines = 30;

    /// <summary>Diacritics allowed to stack on one base character; beyond this is Zalgo abuse.</summary>
    private const int MaxCombiningMarks = 2;

    /// <summary>Zero-width joiner / non-joiner — the only formatting marks kept, since they
    /// compose emoji sequences and some scripts.</summary>
    private const int ZeroWidthJoiner = 0x200D;
    private const int ZeroWidthNonJoiner = 0x200C;

    /// <summary>
    /// Produces the cleaned, length-clamped body. Returns <c>false</c> (with <paramref name="body"/>
    /// set to <see cref="string.Empty"/>) when the message carries no meaningful content.
    /// </summary>
    public static bool TryNormalize(string? raw, out string body)
    {
        body = string.Empty;
        if (string.IsNullOrEmpty(raw))
            return false;

        // Unify line endings first so newline collapsing and counting are consistent.
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');

        var sb = new StringBuilder(Math.Min(text.Length, Constants.Chat.MaxMessageLength));
        var newlineRun = 0;   // consecutive '\n' emitted, to collapse blank-line walls
        var markRun = 0;      // consecutive combining marks emitted, to cap Zalgo stacking
        var lineCount = 1;    // lines emitted so far (a fresh body is one line)

        foreach (var rune in text.EnumerateRunes())
        {
            // Stop at the clamp on a rune boundary so surrogate pairs are never split.
            if (sb.Length + rune.Utf16SequenceLength > Constants.Chat.MaxMessageLength)
                break;

            if (rune.Value == '\n')
            {
                markRun = 0;
                if (lineCount >= MaxLines)
                {
                    sb.Append(' '); // line budget spent — keep text flowing without adding height
                    continue;
                }
                if (newlineRun >= MaxConsecutiveNewlines)
                    continue; // collapse a wall of blank lines
                newlineRun++;
                lineCount++;
                sb.Append('\n');
                continue;
            }

            newlineRun = 0;
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                    // '\r'/'\n' already handled above; a tab becomes a space, all else is dropped.
                    if (rune.Value == '\t')
                        sb.Append(' ');
                    markRun = 0;
                    continue;

                case UnicodeCategory.Format:
                    // Drop bidi overrides, word joiners, BOM and every other invisible formatting
                    // mark; keep only the joiners that compose emoji / scripts.
                    if (rune.Value is ZeroWidthJoiner or ZeroWidthNonJoiner)
                        Append(sb, rune);
                    continue;

                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                    if (markRun >= MaxCombiningMarks)
                        continue; // drop stacked diacritics beyond the cap (Zalgo)
                    markRun++;
                    Append(sb, rune);
                    continue;

                default:
                    markRun = 0;
                    Append(sb, rune);
                    continue;
            }
        }

        body = sb.ToString().Trim();
        return body.Length > 0;
    }

    private static void Append(StringBuilder sb, Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        var written = rune.EncodeToUtf16(buffer);
        sb.Append(buffer[..written]);
    }
}
