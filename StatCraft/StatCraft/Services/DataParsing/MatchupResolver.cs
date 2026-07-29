using StatCraft.Models.GameData;
using StatCraft.ViewModels;

namespace StatCraft.Services.DataParsing
{
    internal static class MatchupResolver
    {
        internal static (Race Player, Race Opponent)? FromPlayerAndOpponents(char playerRace, GamePlayer[] opponents)
        {
            if (opponents.Length == 0)
                return null;

            Race? player = ParseRace(playerRace);
            Race? opponent = ParseRace(opponents[0].Race);
            if (player == null || opponent == null)
                return null;

            return (player.Value, opponent.Value);
        }

        private static Race? ParseRace(char race) => race switch
        {
            'Z' => Race.Z,
            'T' => Race.T,
            'P' => Race.P,
            _ => null,
        };
    }
}
