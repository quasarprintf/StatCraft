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
    [ObservableProperty] private decimal? _mmr;

    internal OpponentRowViewModel(GamePlayer player, GameDataRepository repository)
    {
        _player = player;
        _repository = repository;
        _mmr = player.ReplayPlayer.Mmr.Mmr;
        _player.ReplayPlayer.Mmr.MmrChanged += PlayerMmrChanged;
    }

    private void PlayerMmrChanged(object? sender, EventArgs e)
    {
        Mmr = _player.ReplayPlayer.Mmr.Mmr;
    }

    partial void OnMmrChanged(decimal? value)
    {
        long baseline = _player.ReplayPlayer.Mmr.EstimatedMmr ?? _player.ReplayPlayer.Mmr.ParsedMmr;
        long? newOverride = value == null || (long)value.Value == baseline ? null : (long)value.Value;

        if (_player.ReplayPlayer.Mmr.OverrideMmr != newOverride)
        {
            _player.ReplayPlayer.Mmr.OverrideMmr = newOverride;
            _repository.UpdateGamePlayerOverrideMmr(_player.ReplayPlayer.GamePlayerId!.Value, newOverride);
        }

        if (value == null)
            Mmr = baseline;
    }
}
