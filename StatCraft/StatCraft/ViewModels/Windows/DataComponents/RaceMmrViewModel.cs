using System.Collections.Generic;
using Avalonia.Media;
using StatCraft.Models.GameData.Race;
using StatCraft.Styles;

namespace StatCraft.ViewModels
{
    // One ladder's current rating for the active session's profile. SC2 rates each race independently —
    // and Random separately again — so the header shows one of these per ladder the player has actually
    // placed in.
    public class RaceMmrViewModel
    {
        public LadderRace Race { get; }
        public long Mmr { get; }

        // What this ladder sat at when the session began, so the header can show how far it has moved
        // since. Null for a ladder that wasn't placed at session start — there's no baseline to
        // measure against, so no change is shown rather than a misleading one.
        public long? SessionStartMmr { get; }

        public long? SessionChange => SessionStartMmr is { } start ? Mmr - start : null;

        // Rendered by ColoredTextBlock as e.g. "P: 5239" in the race's colour followed by "(+24)" in
        // win/loss colour — only the delta is tinted, matching how the games table shows per-game MMR.
        public IReadOnlyList<ColoredCharacter> Characters { get; }

        internal RaceMmrViewModel(LadderRace race, long mmr, long? sessionStartMmr)
        {
            Race = race;
            Mmr = mmr;
            SessionStartMmr = sessionStartMmr;

            List<ColoredCharacter> characters = [new ColoredCharacter($"{race.Display()}: {mmr}", RaceColor(race))];

            // Suppressed while the rating hasn't actually moved — "(+0)" on every ladder for the whole
            // start of a session is noise, not information.
            if (SessionChange is { } change && change != 0)
                characters.Add(new ColoredCharacter($"({change:+#;-#;0})", change > 0 ? Styles.Colors.WinGreen : Styles.Colors.LossRed));

            Characters = characters;
        }

        // Random has no race colour of its own, so it stays neutral.
        private static IBrush RaceColor(LadderRace race) => race switch
        {
            LadderRace.Protoss => Styles.Colors.ProtossGreen,
            LadderRace.Terran => Styles.Colors.TerranBlue,
            LadderRace.Zerg => Styles.Colors.ZergRed,
            _ => Brushes.Gray,
        };
    }
}
