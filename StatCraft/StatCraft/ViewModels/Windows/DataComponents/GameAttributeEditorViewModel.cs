using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData.Builds;

namespace StatCraft.ViewModels
{
    // A per-game editable value for one build-tree attribute. Deliberately decoupled from the shared
    // BuildAttribute template instance (only read from it for display) so editing a game's value here
    // never mutates the template's own default value used by BuildsPage and every other game.
    public partial class GameAttributeEditorViewModel : ObservableObject
    {
        private readonly BuildAttribute _template;

        internal int BuildAttributeId => _template.Id;
        public string Name => _template.Name;
        public AttributeType Type => _template.Type;
        public ObservableCollection<string> ValueOptions => _template.ValueOptions;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;

        [ObservableProperty] private decimal _numericValue;
        [ObservableProperty] private bool _boolValue;
        [ObservableProperty] private decimal _percentValue;
        [ObservableProperty] private string? _selectedValue;

        internal GameAttributeEditorViewModel(BuildAttribute template)
        {
            _template = template;

            // Seed from the template's own configured default (set on the Builds tab) so a value that's
            // never been explicitly set for this player still shows something meaningful instead of the
            // field's bare 0/false/null default. RebuildAttributeEditors overwrites this via ApplyValue
            // when a cached GameAttributeValue exists for this player.
            _numericValue = template.NumericValue;
            _boolValue = template.BoolValue;
            _percentValue = template.PercentValue;
            _selectedValue = template.SelectedValue;
        }

        internal string SerializeValue() =>
            BuildAttributeValueSerializer.Serialize(Type, NumericValue, BoolValue, PercentValue, SelectedValue);

        internal void ApplyValue(string value)
        {
            BuildAttributeValueSerializer.ParsedValue parsed = BuildAttributeValueSerializer.Parse(Type, value);
            switch (Type)
            {
                case AttributeType.Numeric:
                    NumericValue = parsed.NumericValue;
                    break;
                case AttributeType.Bool:
                    BoolValue = parsed.BoolValue;
                    break;
                case AttributeType.Percent:
                    PercentValue = parsed.PercentValue;
                    break;
                case AttributeType.Values:
                    SelectedValue = parsed.SelectedValue;
                    break;
            }
        }
    }
}
