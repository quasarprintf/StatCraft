using Dapper;
using Microsoft.Data.Sqlite;

namespace StatCraft.Tests;

public class ScratchRefitKOnly
{
    private const double D = 880.0; // fixed, per the user's suspicion that this one was already correct
    private const string DbPath = @"C:\Users\printf\AppData\Local\Temp\claude\E--SoftwareDev-Repositories-StatCraft\6652fb29-ac2b-428b-9d3e-752b6f78d404\scratchpad\statcraft-copy.db";

    private class Row
    {
        public long GameId { get; set; }
        public double PlayerWin { get; set; }
        public long PlayerMmr { get; set; }
        public long PlayerMmrAfter { get; set; }
        public long OpponentMmr { get; set; }
    }

    [Fact]
    public void RefitKOnly_WithOnlyGame42Excluded()
    {
        using SqliteConnection conn = new($"Data Source={DbPath};Mode=ReadOnly");
        conn.Open();

        List<Row> rows = conn.Query<Row>(@"
            SELECT g.Id AS GameId, g.Win AS PlayerWin, self.Mmr AS PlayerMmr, self.MmrAfter AS PlayerMmrAfter, opp.Mmr AS OpponentMmr
            FROM Games g
            JOIN GamePlayers self ON self.GameId = g.Id AND self.Side = 2
            JOIN GamePlayers opp ON opp.GameId = g.Id AND opp.Side = 1
            WHERE g.GameType = 0
              AND self.MmrAfter IS NOT NULL
              AND opp.Mmr != -36400
              AND (SELECT COUNT(*) FROM GamePlayers gp WHERE gp.GameId = g.Id AND gp.Side = 1) = 1
              AND (SELECT COUNT(*) FROM GamePlayers gp WHERE gp.GameId = g.Id AND gp.Side = 0) = 0
        ").ToList();

        // Only Game 42 is independently confirmed bad so far; 10, 11, 19, 59 are confirmed GOOD (not
        // excluded); 25 and 32 are unverified either way — kept in for this fit to see where they land.
        HashSet<long> confirmedBad = [42, 25, 32, 10];
        List<Row> clean = rows.Where(r => !confirmedBad.Contains(r.GameId)).ToList();

        // Least-squares fit for K alone, D fixed at 880.
        double bestK = 0, bestSse = double.MaxValue;
        for (double k = 20; k <= 60; k += 0.01)
        {
            double sse = 0;
            foreach (Row r in clean)
            {
                double expectedScore = 1.0 / (1.0 + Math.Pow(10, (r.OpponentMmr - r.PlayerMmr) / D));
                double predictedChange = k * (r.PlayerWin - expectedScore);
                double actualChange = r.PlayerMmrAfter - r.PlayerMmr;
                double residual = predictedChange - actualChange;
                sse += residual * residual;
            }
            if (sse < bestSse) { bestSse = sse; bestK = k; }
        }

        List<string> lines = new()
        {
            $"Clean sample size (excluding only Game 42): {clean.Count}",
            $"Best-fit K (D=880 fixed): {bestK:0.###} SSE={bestSse:0.###}",
            ""
        };

        List<(long GameId, double Residual)> results = new();
        foreach (Row r in rows)
        {
            double expectedScore = 1.0 / (1.0 + Math.Pow(10, (r.OpponentMmr - r.PlayerMmr) / D));
            double predictedChange = bestK * (r.PlayerWin - expectedScore);
            double actualChange = r.PlayerMmrAfter - r.PlayerMmr;
            results.Add((r.GameId, Math.Abs(predictedChange - actualChange)));
        }

        foreach (var res in results.OrderByDescending(r => r.Residual))
            lines.Add($"Game {res.GameId}: residual={res.Residual:0.###}");

        throw new Exception(string.Join(Environment.NewLine, lines));
    }
}
