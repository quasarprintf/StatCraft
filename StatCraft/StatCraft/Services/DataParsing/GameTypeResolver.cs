using StatCraft.Models.GameData;

namespace StatCraft.Services.DataParsing
{
    internal static class GameTypeResolver
    {
        internal static GameType Resolve(ParsedReplayData replay, long? lastKnownRankedMmr)
        {
            if (!replay.IsMatchmade) //directly from replay file
                return GameType.Custom;

            //ranked mmr is unknown, use default value
            if (lastKnownRankedMmr is not { } known)
                return GameType.Ranked; //TODO: re-evaluate default value of ranked

            //if mmr doesn't match ranked mmr, it must be unranked. Otherwise, assume ranked
            return known == replay.Player.Mmr.ParsedMmr ? GameType.Ranked : GameType.Unranked;
        }
    }
}
