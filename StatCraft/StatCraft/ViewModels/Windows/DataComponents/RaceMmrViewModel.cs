using Avalonia.Media;
using StatCraft.Models.GameData.Race;

namespace StatCraft.ViewModels
{
    // One ladder's current rating for the active session's profile. SC2 rates each race independently —
    // and Random separately again — so the header shows one of these per ladder the player has actually
    // placed in.
    public class RaceMmrViewModel
    {
        public LadderRace Race { get; }
        public long Mmr { get; }

        public string Label => $"{Race}: {Mmr}";

        // Random has no race colour of its own, so it stays neutral.
        public IBrush Color => Race switch
        {
            LadderRace.P => Styles.Colors.ProtossGreen,
            LadderRace.T => Styles.Colors.TerranBlue,
            LadderRace.Z => Styles.Colors.ZergRed,
            _ => Brushes.Gray,
        };

        internal RaceMmrViewModel(LadderRace race, long mmr)
        {
            Race = race;
            Mmr = mmr;
        }
    }
}
