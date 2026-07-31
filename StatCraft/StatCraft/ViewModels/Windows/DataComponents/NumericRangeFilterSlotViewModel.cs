using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels
{
    // An extra filter whose criteria is a numeric [Min, Max] range (opponent MMR).
    public sealed partial class NumericRangeFilterSlotViewModel : FilterSlotViewModel
    {
        [ObservableProperty] private long? _min;
        [ObservableProperty] private long? _max;

        internal NumericRangeFilterSlotViewModel(string title) : base(title)
        {
        }

        partial void OnMinChanged(long? value) => RaiseChanged();
        partial void OnMaxChanged(long? value) => RaiseChanged();

        public override void Clear()
        {
            Min = null;
            Max = null;
        }
    }
}
