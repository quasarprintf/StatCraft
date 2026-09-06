using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels.Windows.DataComponents
{
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
    }
}
