using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;

namespace StatCraft.ViewModels.Windows.Filters;

// Non-generic base is all the reusable CheckboxFilterDropdown view needs (Label/IsChecked); the
// generic subclass carries the strongly-typed value each filter dimension actually filters on.
public abstract partial class CheckboxFilterOptionViewModel : ObservableObject
{
    public string Label { get; protected init; } = "";

    [ObservableProperty] private bool _isChecked;
}

public sealed class CheckboxFilterOptionViewModel<T> : CheckboxFilterOptionViewModel
{
    public T Value { get; }

    internal CheckboxFilterOptionViewModel(T value, string label)
    {
        Value = value;
        Label = label;
    }
    public BoolFilter<T> GetFilter(bool? acceptNull = null)
    {
        return new BoolFilter<T>(o => o?.Equals(Value))
        {
            AcceptNull = acceptNull ?? false
        };
    }
}
