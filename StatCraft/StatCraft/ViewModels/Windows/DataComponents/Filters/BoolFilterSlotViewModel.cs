using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // An extra filter for a single Bool map attribute. A three-state checkbox rather than a Yes/No
    // checkbox dropdown, since a Bool attribute never has more than two real values to pick between —
    // null (indeterminate) means no constraint on this dimension, matching both true and false.
    public sealed partial class BoolFilterSlotViewModel : FilterSlotViewModel
    {
        [ObservableProperty] private bool? _value;

        internal BoolFilterSlotViewModel(string title) : base(title)
        {
        }

        partial void OnValueChanged(bool? value) => RaiseChanged();

        public override void Clear() => Value = null;
    }
}
