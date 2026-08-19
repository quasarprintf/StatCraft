using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Race
{
    public enum Race { Zerg, Terran, Protoss }

    public static class RaceExtensions
    {
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
}
