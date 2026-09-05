namespace StatCraft.Models.GameData
{
    // A GamePlayer's MMR going into the game, from up to three sources in priority order. ParsedMmr is
    // always the raw value read straight from the replay and is never itself overwritten; EstimatedMmr is
    // set instead, alongside it, whenever OpponentMmrEstimator judges ParsedMmr implausible given the
    // tracked player's own observed MmrChange (see ReplayImportService.TryCorrectOpponentMmr). OverrideMmr
    // isn't populated by anything yet — a future manual-correction path.
    internal class PlayerMmr
    {
        public required long ParsedMmr { get; set; }
        public long? EstimatedMmr { get; set; }
        public long? OverrideMmr { get; set; }

        public long Mmr => OverrideMmr ?? EstimatedMmr ?? ParsedMmr;
    }
}
