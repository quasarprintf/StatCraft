namespace StatCraft.Models.GameData
{
    public enum GameOutcome { Win, Loss, Draw }

    public static class GameOutcomeExtensions
    {
        // Matches the win-value convention already established for ParsedReplayData.Win: 1 = win, 0 = loss
        // anything else (in practice 0.5) = draw.
        internal static GameOutcome FromWin(decimal win) => win == 1m ? GameOutcome.Win : win == 0m ? GameOutcome.Loss : GameOutcome.Draw;
    }
}
