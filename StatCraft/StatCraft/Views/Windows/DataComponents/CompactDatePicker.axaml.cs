using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace StatCraft.Views.Windows.DataComponents
{
    public partial class CompactDatePicker : UserControl
    {
        private const string DateFormat = "MM/dd/yyyy";

        public static readonly StyledProperty<DateTime?> SelectedDateProperty =
            AvaloniaProperty.Register<CompactDatePicker, DateTime?>(nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

        public DateTime? SelectedDate
        {
            get => GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        public CompactDatePicker()
        {
            InitializeComponent();

            PropertyChanged += (_, e) =>
            {
                if (e.Property == SelectedDateProperty)
                    RefreshTextBox();
            };
            RefreshTextBox();

            // Closes the flyout as soon as a day is picked, instead of requiring a click elsewhere to
            // dismiss it. Harmless when the flyout isn't open (e.g. this fires for external/bound
            // updates to SelectedDate too) since Hide() on an already-closed Flyout is a no-op.
            CalendarControl.PropertyChanged += (_, e) =>
            {
                if (e.Property == Calendar.SelectedDateProperty)
                    ToggleButton.Flyout?.Hide();
            };

            DateTextBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTextBox();
                    e.Handled = true;
                }
            };
            DateTextBox.LostFocus += (_, _) => CommitTextBox();
        }

        private void RefreshTextBox() => DateTextBox.Text = SelectedDate?.ToString(DateFormat) ?? "";

        // Typing an invalid/incomplete date and leaving the box just reverts to the last valid value,
        // rather than leaving SelectedDate out of sync with what's displayed.
        private void CommitTextBox()
        {
            if (DateTime.TryParse(DateTextBox.Text, out DateTime parsed))
                SelectedDate = parsed.Date;
            else
                RefreshTextBox();
        }
    }
}
