using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels
{
    // An extra filter whose criteria is a numeric [Min, Max] range — opponent MMR on the Data tab, and
    // any Numeric or Percent map attribute on the Maps tab.
    //
    // decimal rather than long because map attribute values are decimal; MMR is integral, so the Data
    // tab narrows back to long when it builds its criteria.
    public sealed partial class NumericRangeFilterSlotViewModel : FilterSlotViewModel
    {
        [ObservableProperty] private decimal? _min;
        [ObservableProperty] private decimal? _max;

        internal NumericRangeFilterSlotViewModel(string title) : base(title)
        {
        }

        partial void OnMinChanged(decimal? value) => RaiseChanged();
        partial void OnMaxChanged(decimal? value) => RaiseChanged();

        public override void Clear()
        {
            Min = null;
            Max = null;
        }
    }
}
