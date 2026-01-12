namespace Fotbalek.Web.Services;

public class EloService
{
    public (int change1, int change2) CalculateEloChange(int team1Elo, int team2Elo, bool team1Won)
    {
        double expected1 = 1.0 / (1.0 + Math.Pow(10, (team2Elo - team1Elo) / 400.0));
        double actual1 = team1Won ? 1.0 : 0.0;

        // Calculate change for team 1 and negate for team 2 to ensure zero-sum
        int change1 = (int)Math.Round(Constants.Elo.KFactor * (actual1 - expected1));
        int change2 = -change1;

        return (change1, change2);
    }

    public int ApplyEloChange(int currentElo, int change)
    {
        var newElo = currentElo + change;
        return Math.Max(Constants.Elo.MinimumRating, newElo);
    }

    public int GetTeamElo(int player1Elo, int player2Elo)
    {
        return (player1Elo + player2Elo) / 2;
    }
}
