using StatCraft.Models.GameData;
using StatCraft.ViewModels;

namespace StatCraft.Services.DataParsing
{
    internal static class MatchupResolver
    {
        internal static Matchup? FromOpponents(GamePlayer[] opponents)
        {
            if (opponents.Length == 0)
                return null;

            return opponents[0].Race switch
            {
                'P' => Matchup.VsP,
                'T' => Matchup.VsT,
                'Z' => Matchup.VsZ,
                _ => null,
            };
        }
    }
}
