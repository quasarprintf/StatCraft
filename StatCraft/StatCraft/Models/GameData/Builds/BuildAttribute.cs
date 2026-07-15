using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace StatCraft.Models.GameData.Builds
{
    public partial class BuildAttribute : ObservableObject
    {
        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNumeric), nameof(IsBool), nameof(IsPercent), nameof(IsValues))]
        private AttributeType _type = AttributeType.Numeric;

        [ObservableProperty] private decimal _numericValue;
        [ObservableProperty] private bool _boolValue;
        [ObservableProperty] private decimal _percentValue;

        public ObservableCollection<string> ValueOptions { get; } = [];
        [ObservableProperty] private string? _selectedValue;
        [ObservableProperty] private string _newOptionText = string.Empty;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;

        [RelayCommand]
        private void AddOption()
        {
            if (string.IsNullOrWhiteSpace(NewOptionText)) return;
            ValueOptions.Add(NewOptionText.Trim());
            NewOptionText = string.Empty;
        }

        [RelayCommand]
        private void RemoveOption(string option) => ValueOptions.Remove(option);
    }
}
