using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Models.GameData.Maps
{
    public partial class Map : ObservableObject
    {
        public int Id { get; set; }
        [ObservableProperty] private string _name = string.Empty;
        public ObservableCollection<AttributeValue> AttributeValues { get; } = [];
    }
}
