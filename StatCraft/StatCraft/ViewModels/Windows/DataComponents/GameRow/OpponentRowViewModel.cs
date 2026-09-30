using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Services.DatabaseRepository;
using System;

namespace StatCraft.ViewModels.Windows.DataComponents.GameRow;

// One opponent's row in the Data tab's Opponent column — name/clan plus an editable MMR. A compact,
// public-typed stand-in for GamePlayer (internal), matching GameDataRowViewModel's own rule against
// leaking internal model types through a public surface.
public partial class OpponentRowViewModel : ViewModelBase
{
    private readonly GamePlayer _player;
    private readonly GameDataRepository _repository;

    public string Name => _player.ReplayPlayer.Name;
    public string FormattedClan => _player.ReplayPlayer.FormattedClan;

    //mmr currently being displayed, not guaranteed to match player.mmr
    [ObservableProperty] public partial decimal? Mmr { get; set; }

    // Whether the MMR on display is OpponentMmrEstimator's inference rather than a value the replay
    // itself reported or one typed in here — the view tints the field while it is (see DataPage.axaml).
    public bool IsMmrEstimated =>
        _player.ReplayPlayer.Mmr.OverrideMmr == null && _player.ReplayPlayer.Mmr.EstimatedMmr != null;

    internal OpponentRowViewModel(GamePlayer player, GameDataRepository repository)
    {
        _player = player;
        _repository = repository;
        Mmr = player.ReplayPlayer.Mmr.Mmr;
        _player.ReplayPlayer.Mmr.MmrChanged += PlayerMmrChanged;
    }

    private void PlayerMmrChanged(object? sender, EventArgs e)
    {
        Mmr = _player.ReplayPlayer.Mmr.Mmr;
        // Which of the three MMR sources is in effect can change with it, including when OnMmrChanged
        // below writes OverrideMmr, since that write raises MmrChanged too.
        OnPropertyChanged(nameof(IsMmrEstimated));
    }

    partial void OnMmrChanged(decimal? value)
    {
        long baseline = _player.ReplayPlayer.Mmr.EstimatedMmr ?? _player.ReplayPlayer.Mmr.ParsedMmr;
        long? newOverride = value == null || (long)value.Value == baseline ? null : (long)value.Value;

        if (_player.ReplayPlayer.Mmr.OverrideMmr != newOverride)
        {
            _player.ReplayPlayer.Mmr.OverrideMmr = newOverride;
            _repository.UpdateGamePlayerOverrideMmr(_player.GamePlayerId!.Value, newOverride);
        }

        if (value == null)
            Mmr = baseline;
    }
}
