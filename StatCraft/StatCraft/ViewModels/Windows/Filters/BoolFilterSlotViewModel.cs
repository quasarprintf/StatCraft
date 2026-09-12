using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;
using System;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IBoolFilterSlotViewModel : IFilterSlotViewModel
{
    bool? Value { get; set; }
}
// An extra filter for a single Bool map attribute. A three-state checkbox rather than a Yes/No
// checkbox dropdown, since a Bool attribute never has more than two real values to pick between —
// null (indeterminate) means no constraint on this dimension, matching both true and false.
public sealed partial class BoolFilterSlotViewModel<T> : FilterSlotViewModel<T,bool?>, IBoolFilterSlotViewModel
{
    [ObservableProperty] private bool? _value;

    internal BoolFilterSlotViewModel(string title, Func<T,bool?> filteredPropertyMap) : base(title, filteredPropertyMap)
    {
    }

    public override BoolFilter<T> GetFilter()
    {
        return new BoolFilter<T>(FilteredPropertyMap)
        {
            AcceptNull = IncludeUnset,
            FilterValue = Value
        };
    }

    partial void OnValueChanged(bool? value) => RaiseChanged();

    public override void Clear() => Value = null;
}
