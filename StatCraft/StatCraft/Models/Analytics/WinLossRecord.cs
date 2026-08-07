using StatCraft.Models.GameData;
using System.Collections.Generic;
using System.Globalization;

namespace StatCraft.Models.Analytics
{
    internal class WinLossRecord
    {
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }

        public int Total => Wins + Losses + Draws;

        public double? WinRate => Total == 0 ? null : (Wins + (Draws * 0.5)) / Total;

        public string Label
        {
            get
            {
                if (WinRate is not { } rate)
                    return "";

                // draw count only included when nonzero
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
