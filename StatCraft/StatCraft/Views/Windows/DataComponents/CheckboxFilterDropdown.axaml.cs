using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace StatCraft.Views.Windows.DataComponents
{
    // A generic checkbox-list dropdown, decoupled from any one ViewModel shape via plain
    // AvaloniaProperties (rather than binding directly to e.g. CheckboxFilterSlotViewModel) so it can
    // serve both the always-visible Profile filter (CheckboxFilterOptionViewModel<Sc2Profile>) and any
    // CheckboxFilterSlotViewModel (Map/Matchup/Outcome/Build).
    public partial class CheckboxFilterDropdown : UserControl
    {
        public static readonly StyledProperty<IEnumerable?> OptionsProperty =
            AvaloniaProperty.Register<CheckboxFilterDropdown, IEnumerable?>(nameof(Options));

        public static readonly StyledProperty<string?> HeaderProperty =
            AvaloniaProperty.Register<CheckboxFilterDropdown, string?>(nameof(Header));

        public static readonly StyledProperty<bool> ShowSearchProperty =
            AvaloniaProperty.Register<CheckboxFilterDropdown, bool>(nameof(ShowSearch));

        public static readonly StyledProperty<string?> SearchTextProperty =
            AvaloniaProperty.Register<CheckboxFilterDropdown, string?>(nameof(SearchText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<int> ColumnsProperty =
            AvaloniaProperty.Register<CheckboxFilterDropdown, int>(nameof(Columns), defaultValue: 1);

        public IEnumerable? Options
        {
            get => GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        public string? Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public bool ShowSearch
        {
            get => GetValue(ShowSearchProperty);
            set => SetValue(ShowSearchProperty, value);
        }

        public string? SearchText
        {
            get => GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        public int Columns
        {
            get => GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public CheckboxFilterDropdown()
        {
            InitializeComponent();
        }

        // Bound (via MultiBinding) on each option row's IsVisible, so the search box narrows the
        // checkbox list without touching the underlying Options collection or any checked state.
        public static readonly IMultiValueConverter LabelContainsSearchConverter =
            new FuncMultiValueConverter<string?, bool>(MatchesSearch);

        private static bool MatchesSearch(IReadOnlyList<string?> values)
        {
            string? label = values.Count > 0 ? values[0] : null;
            string? search = values.Count > 1 ? values[1] : null;
            return string.IsNullOrEmpty(search) || (label?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
