using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Models.GameData.Maps
{
    public partial class Map : ObservableObject
    {
        public int Id { get; set; }
        [ObservableProperty] private string _name = string.Empty;
        public ObservableCollection<AttributeValue> AttributeValues { get; } = [];

        [RelayCommand]
        public void AddAttribute(AttributeDefinition definition) 
        {
            AttributeValues.Add(definition.DefaultValue.Clone());
        }

        [RelayCommand]
        public void RemoveAttribute(AttributeValue value)
        {
            AttributeValues.Remove(value);
        }
    }
}
