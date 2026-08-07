using StatCraft.Models.GameData;
using System.Collections.Generic;
using System.Globalization;

namespace StatCraft.Models.Analytics
{
    // Win/loss/draw tally over an arbitrary set of games. Derived from whatever the Data tab is
    // currently showing rather than accumulated as games are played, so it answers whatever question
    // the filters are asking — win rate in one matchup, on one map, against one MMR band, and so on.
    internal class WinLossRecord
    {
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }

        public int Total => Wins + Losses + Draws;

        // Draws count as half a win, so they sit in both the numerator and the denominator rather than
        // being discarded. Null when nothing has been played, so callers can show nothing instead of 0%.
        public double? WinRate => Total == 0 ? null : (Wins + (Draws * 0.5)) / Total;

        // Includes the raw record, because a bare percentage is badly misleading on small samples — one
        // win reads as "100%" otherwise. The draw count only appears when there is one.
        public string Label
        {
            get
            {
                if (WinRate is not { } rate)
                    return "";

                string record = Draws == 0 ? $"{Wins}-{Losses}" : $"{Wins}-{Losses}-{Draws}";
                return string.Create(CultureInfo.CurrentCulture, $"{record} ({rate:P0})");
            }
        }

        public WinLossRecord(int wins, int losses, int draws)
        {
            Wins = wins;
            Losses = losses;
            Draws = draws;
        }

        public static WinLossRecord From(IEnumerable<GameOutcome> outcomes)
        {
            int wins = 0, losses = 0, draws = 0;
            foreach (GameOutcome outcome in outcomes)
            {
                switch (outcome)
                {
                    case GameOutcome.Win: wins++; break;
                    case GameOutcome.Loss: losses++; break;
                    default: draws++; break;
                }
            }

            return new WinLossRecord(wins, losses, draws);
        }
    }
}
