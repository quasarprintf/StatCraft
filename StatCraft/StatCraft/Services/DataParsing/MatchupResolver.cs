using StatCraft.Models.GameData;
using StatCraft.ViewModels;

namespace StatCraft.Services.DataParsing
{
    internal static class MatchupResolver
    {
        internal static Matchup? FromPlayerAndOpponents(char playerRace, GamePlayer[] opponents)
        {
            if (opponents.Length == 0)
                return null;

            return (playerRace, opponents[0].Race) switch
            {
                ('Z', 'Z') => Matchup.ZvZ,
                ('Z', 'T') => Matchup.ZvT,
                ('Z', 'P') => Matchup.ZvP,
                ('T', 'Z') => Matchup.TvZ,
                ('T', 'T') => Matchup.TvT,
                ('T', 'P') => Matchup.TvP,
                ('P', 'Z') => Matchup.PvZ,
                ('P', 'T') => Matchup.PvT,
                ('P', 'P') => Matchup.PvP,
                _ => null,
            };
        }
    }
}
