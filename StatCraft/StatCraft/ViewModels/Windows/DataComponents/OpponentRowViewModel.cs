using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // One opponent's row in the Data tab's Opponent column — name/clan plus an editable MMR. A compact,
    // public-typed stand-in for GamePlayer (internal), matching GameDataRowViewModel's own rule against
    // leaking internal model types through a public surface.
    public partial class OpponentRowViewModel : ViewModelBase
    {
        private readonly GamePlayer _player;
        private readonly GameDataRepository _repository;

        public string Name => _player.Name;
        public string FormattedClan => _player.FormattedClan;

        // Editable. Typing a value other than the current EstimatedMmr ?? ParsedMmr baseline records it
        // as OverrideMmr; clearing the field — or typing that same baseline value back in — removes the
        // override, and the field snaps back to showing the baseline again (blank is only ever a
        // transient editing gesture, never a stored state).
        [ObservableProperty] private decimal? _mmr;

        internal OpponentRowViewModel(GamePlayer player, GameDataRepository repository)
        {
            _player = player;
            _repository = repository;
            _mmr = player.Mmr.Mmr;
        }

        partial void OnMmrChanged(decimal? value)
        {
            long baseline = _player.Mmr.EstimatedMmr ?? _player.Mmr.ParsedMmr;
            long? newOverride = value == null || (long)value.Value == baseline ? null : (long)value.Value;

            if (_player.Mmr.OverrideMmr != newOverride)
            {
                _player.Mmr.OverrideMmr = newOverride;
                _repository.UpdateGamePlayerOverrideMmr(_player.GamePlayerId!.Value, newOverride);
            }

            if (value == null)
                Mmr = baseline;
        }
    }
}
