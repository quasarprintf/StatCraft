using StatCraft.Models.GameData;
using StatCraft.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace StatCraft.Services.DataParsing
{
    internal static class MatchupResolver
    {
        internal static Matchups FromOpponents(GamePlayer[] opponents)
        {
            Matchups matchups = Matchups.None;
            foreach (var opponent in opponents)
            {
                matchups |= ParseMatchup(opponent.Race);
            }
            return matchups;
        }

        public static Race? AsRace(this char raw) => raw switch
        {
            'Z' => Race.Z,
            'T' => Race.T,
            'P' => Race.P,
            _ => null
        };
        private static Matchups ParseMatchup(char race) => race switch
        {
            'Z' => Matchups.VsZ,
            'T' => Matchups.VsT,
            'P' => Matchups.VsP,
            _ => Matchups.None,
        };
    }
}
