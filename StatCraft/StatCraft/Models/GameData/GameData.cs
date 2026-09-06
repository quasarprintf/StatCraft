using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace StatCraft.Models.GameData;

internal class GameData : IAttributedObject
{
    public int? GameId { get; set; }
    public int Sc2ProfileId { get; set; }

    public Map? Map { get; set; }

    public GameType GameType { get; set; }
    public required ParsedReplayData ReplayData { get; set; }
    public ObservableCollection<AttributeValue> AttributeValues { get; set; } = [];
    public string Notes { get; set; } = string.Empty;

    public void AddAttribute(AttributeDefinition definition) 
    {
        AttributeValues.Add(definition.DefaultValue.Clone());
    }

    public void RemoveAttribute(AttributeValue value)
    {
        AttributeValues.Remove(value);
    }
}
