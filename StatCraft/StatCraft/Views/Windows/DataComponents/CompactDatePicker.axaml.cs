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
        }
    }
}
