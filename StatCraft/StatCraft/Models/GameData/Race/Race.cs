using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Race;

public enum Race { Zerg, Terran, Protoss }

public static class RaceExtensions
{
    // Which of a build's matchup flags this race is the opponent in.
    public static Matchups AsMatchup(this Race race) => race switch
    {
        Race.Zerg => Matchups.VsZ,
        Race.Terran => Matchups.VsT,
        Race.Protoss => Matchups.VsP,
        _ => Matchups.None,
    };

    public static string Display(this Race race)
    {
        switch (race)
        {
            case Race.Zerg:
                return "Z";
            case Race.Terran:
                return "T";
            case Race.Protoss:
                return "P";
            default:
                return " ";
        }
    }
}
