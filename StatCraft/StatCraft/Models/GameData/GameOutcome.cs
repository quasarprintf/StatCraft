namespace StatCraft.Models.GameData
{
    public enum GameOutcome { Win, Loss, Draw }

    public static class GameOutcomeExtensions
    {
        internal static GameOutcome FromWin(decimal win) => win == 1m ? GameOutcome.Win : win == 0m ? GameOutcome.Loss : GameOutcome.Draw;
    }
}
