using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IDateRangeFilterSlotViewModel : IFilterSlotViewModel
{
    DateTime? FromDate { get; set; }
    DateTime? ToDate { get; set; }
}

// A filter whose criteria is a [FromDate, ToDate] range of calendar days, both ends inclusive — the
// same shape as the Data tab's always-visible date range. Either end may be left empty to leave that
// side open.
//
// DateTime (not DateTimeOffset) because Calendar.SelectedDate — which CompactDatePicker wraps — is
// DateTime?. Comparison is by calendar day only: the bounds and each candidate's mapped value are all
// reduced to .Date, so a candidate any time on ToDate still matches. Converting a timestamp into the
// right time zone first (e.g. ToLocalTime) is up to the caller's filteredPropertyMap.
public sealed partial class DateRangeFilterSlotViewModel<T> : FilterSlotViewModel<T,DateTime?>, IDateRangeFilterSlotViewModel
{
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;

    internal DateRangeFilterSlotViewModel(string title, Func<T,DateTime?> filteredPropertyMap) : base(title, filteredPropertyMap)
    {
    }

    public override AndFilter<T> GetFilter()
    {
        Func<T,DateTime?> mapToDay = t => FilteredPropertyMap(t)?.Date;
        DateTimeFilter<T> lowerBound = new DateTimeFilter<T>(mapToDay)
        {
            FilterValue = FromDate?.Date
        }.SetMatchLowerBound();
        DateTimeFilter<T> upperBound = new DateTimeFilter<T>(mapToDay)
        {
            FilterValue = ToDate?.Date
        }.SetMatchUpperBound();
        return new AndFilter<T>([lowerBound, upperBound])
        {
            AcceptNull = IncludeUnset
        };
    }

    partial void OnFromDateChanged(DateTime? value) => RaiseChanged();
    partial void OnToDateChanged(DateTime? value) => RaiseChanged();

    public override void Clear()
    {
        FromDate = null;
        ToDate = null;
    }
}
