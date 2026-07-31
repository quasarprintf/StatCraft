using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace StatCraft.Views
{
    public partial class CompactDatePicker : UserControl
    {
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

            // Closes the flyout as soon as a day is picked, instead of requiring a click elsewhere to
            // dismiss it. Harmless when the flyout isn't open (e.g. this fires for external/bound
            // updates to SelectedDate too) since Hide() on an already-closed Flyout is a no-op.
            CalendarControl.PropertyChanged += (_, e) =>
            {
                if (e.Property == Calendar.SelectedDateProperty)
                    ToggleButton.Flyout?.Hide();
            };
        }
    }
}
