namespace StatCraft.Models.Util
{
    public class AppSettingsData
    {
        public string? BaseReplayFolderPath { get; set; }

        // When on, ally/opponent tab headers on the Data tab are colored by side instead of by each
        // player's actual in-game color — see Styles.Colors.AllyColor/OpponentColor.
        public bool UseTeamColors { get; set; }
    }
}
