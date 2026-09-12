using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;
using System;

namespace StatCraft.ViewModels.Windows.Filters;

// Non-generic base is all the reusable CheckboxFilterDropdown view needs (Label/IsChecked); the
// generic subclass carries the strongly-typed value each filter dimension actually filters on.
public interface ICheckboxFilterOptionViewModel
{
    string Label { get; }
    bool IsChecked { get; set; }
}

public partial class CheckboxFilterOptionViewModel<T> : ObservableObject, ICheckboxFilterOptionViewModel
{
    public string Label { get; protected init; } = "";
    [ObservableProperty] private bool _isChecked;

    public T Value { get; }

    internal CheckboxFilterOptionViewModel(T value, string label)
    {
        Value = value;
        Label = label;
    }
}
