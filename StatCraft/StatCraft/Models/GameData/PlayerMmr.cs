using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace StatCraft.Models.GameData;

// A GamePlayer's MMR going into the game, from up to three sources in priority order. ParsedMmr is
// always the raw value read straight from the replay and is never itself overwritten; EstimatedMmr is
// set instead, alongside it, whenever OpponentMmrEstimator judges ParsedMmr implausible given the
// tracked player's own observed MmrChange (see ReplayImportService.TryCorrectOpponentMmr). OverrideMmr
// isn't populated by anything yet — a future manual-correction path.
public class PlayerMmr
{
    public event EventHandler? MmrChanged;
    public required long ParsedMmr 
    { 
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            if (EstimatedMmr == null && OverrideMmr == null)
                MmrChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public long? EstimatedMmr 
    {
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            if (OverrideMmr == null)
                MmrChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public long? OverrideMmr 
    { 
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            MmrChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public long Mmr => OverrideMmr ?? EstimatedMmr ?? ParsedMmr;
}
