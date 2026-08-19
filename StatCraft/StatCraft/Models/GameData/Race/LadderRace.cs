namespace StatCraft.Models.GameData.Race
{
    // The race a ladder rating belongs to. Deliberately separate from Race: SC2 rates Random as its own
    // ladder with its own MMR, but Random is not a race a build or a matchup can be filed under — a
    // replay records the race that actually spawned, with Random tracked as a flag alongside it. Folding
    // Random into Race would turn the Builds tab's per-race buckets and the 3x3 matchup grid into
    // something they aren't.
    public enum LadderRace { Zerg, Terran, Protoss, Random }

    public static class LadderRaceExtensions
    {
        // Which ladder a game was actually played on. Queueing as Random earns Random MMR regardless of
        // which race the player then spawned as, so the flag wins over the replay's recorded race.
        public static LadderRace? FromPlayer(char spawnedRace, bool random)
        {
            if (random)
                return LadderRace.Random;
            switch (spawnedRace)
            {
                case 'Z':
                    return LadderRace.Zerg;
                case 'T':
                    return LadderRace.Terran;
                case 'P':
                    return LadderRace.Protoss;
                default:
                    return null;
            };
        }

        public static string Display(this LadderRace race)
        {
            switch (race)
            {
                case LadderRace.Zerg:
                    return "Z";
                case LadderRace.Terran:
                    return "T";
                case LadderRace.Protoss:
                    return "P";
                case LadderRace.Random:
                    return "R";
                default:
                    return " ";
            }
        }
    }
}
