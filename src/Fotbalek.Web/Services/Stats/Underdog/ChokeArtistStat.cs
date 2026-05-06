using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats.Underdog;

/// <summary>
/// Most losses where the player's team had a combined ELO at least 100 higher than the opponents.
/// </summary>
public class ChokeArtistStat : StatBase
{
    private const int EloGap = 100;

    public override string Key => "ChokeArtist";
    public override string Name => "Choke Artist";
    public override string Emoji => "\U0001F633";
    public override StatTheme Theme => StatTheme.Underdog;
    public override string Description => $"Most losses where your team was {EloGap}+ ELO favorites";

    protected override IReadOnlyList<StatHolder> Compute(StatContext context)
    {
        var counts = new Dictionary<int, int>();
        foreach (var match in context.Matches)
        {
            if (!match.TryGetTeams(out var winners, out var losers)) continue;
            var winnersElo = winners.Sum(mp => mp.EloBefore);
            var losersElo = losers.Sum(mp => mp.EloBefore);
            if (losersElo - winnersElo < EloGap) continue;
            foreach (var l in losers)
            {
                counts.TryGetValue(l.PlayerId, out var v);
                counts[l.PlayerId] = v + 1;
            }
        }
        return StatHelpers.TopByValue(counts, context.PlayersById, v => $"{v} chokes");
    }
}
