namespace StatCraft.Models.GameData
{
    // How a game was played. Stored nullable on GameData: games imported before this existed can't be
    // reclassified, because the replay flags it derives from were never persisted.
    //
    // Custom is read straight off the replay (GameOptions.Amm is false for anything not matchmade) and
    // is reliable. Ranked vs Unranked has no such flag — both are matchmade and, in every replay
    // available to check against, both would look identical — so it's inferred by comparing the game's
    // starting MMR against the last known *ranked* MMR for that ladder. See GameTypeResolver.
    public enum GameType { Ranked, Unranked, Custom }
}
