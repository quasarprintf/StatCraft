using Avalonia.Media;
using StatCraft.Models.GameData.Race;

namespace StatCraft.ViewModels
{
    // One race's current ladder rating for the active session's profile. SC2 rates each race
    // independently, so the header shows one of these per race the player has actually placed in.
    public class RaceMmrViewModel
    {
        public Race Race { get; }
        public long Mmr { get; }

        public string Label => $"{Race} {Mmr}";
        public IBrush Color => Race switch
        {
            Race.P => Styles.Colors.ProtossGreen,
            Race.T => Styles.Colors.TerranBlue,
            _ => Styles.Colors.ZergRed,
        };

        internal RaceMmrViewModel(Race race, long mmr)
        {
            Race = race;
            Mmr = mmr;
        }
    }
}
