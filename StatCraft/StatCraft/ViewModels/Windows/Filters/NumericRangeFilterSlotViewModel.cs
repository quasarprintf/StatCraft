using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;

namespace StatCraft.ViewModels.Windows.Filters;

public interface INumericRangeFilterSlotViewModel : IFilterSlotViewModel
{
    decimal? Min { get; set; }
    decimal? Max { get; set; }
}

// An extra filter whose criteria is a numeric [Min, Max] range — opponent MMR on the Data tab, and
// any Numeric or Percent map attribute on the Maps tab.
//
// decimal rather than long because map attribute values are decimal; MMR is integral, so the Data
// tab narrows back to long when it builds its criteria.
public sealed partial class NumericRangeFilterSlotViewModel<T> : FilterSlotViewModel<T,decimal?>, INumericRangeFilterSlotViewModel
{
    [ObservableProperty] private decimal? _min;
    [ObservableProperty] private decimal? _max;

    internal NumericRangeFilterSlotViewModel(string title, Func<T,decimal?> filteredPropertyMap) : base(title, filteredPropertyMap)
    {
    }

    public override AndFilter<T> GetFilter()
    {
        DecimalFilter<T> lowerBound = new DecimalFilter<T>(FilteredPropertyMap)
        {
            FilterValue = Min
        }.SetMatchLowerBound();
        DecimalFilter<T> upperBound = new DecimalFilter<T>(FilteredPropertyMap)
        {
            FilterValue = Max
        }.SetMatchUpperBound();
        return new AndFilter<T>([lowerBound, upperBound])
        {
            AcceptNull = IncludeUnset
        };
    }

    partial void OnMinChanged(decimal? value) => RaiseChanged();
    partial void OnMaxChanged(decimal? value) => RaiseChanged();

    public override void Clear()
    {
        Min = null;
        Max = null;
    }
}
