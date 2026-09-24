using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Replays;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.Models.GameData;

public class GameData : IAttributedObject
{
    public int? GameId { get; set; }
    public int Sc2ProfileId { get; set; }

    public GameType GameType { get; set; }
    public ParsedReplayData ReplayData { get; set; }
    public Dictionary<ReplayPlayer, GamePlayer> PlayerDetails { get; set; }
    public ObservableCollection<AttributeValue> AttributeValues { get; set; } = [];
    public string Notes { get; set; } = string.Empty;

    public GameData(ParsedReplayData replayData)
    {
        ReplayData = replayData;
        PlayerDetails = new Dictionary<ReplayPlayer, GamePlayer>();
        PlayerDetails[replayData.Player] = new GamePlayer(replayData.Player);
        foreach (var ally in replayData.Allies)
            PlayerDetails[ally] = new GamePlayer(ally);
        foreach (var opponent in replayData.Opponents)
            PlayerDetails[opponent] = new GamePlayer(opponent);
    }
    public GameData(ParsedReplayData replayData, Dictionary<ReplayPlayer, GamePlayer> playerDetails)
    {
        ReplayData = replayData;
        PlayerDetails = playerDetails;
    }

    public void AddAttribute(AttributeDefinition definition) 
    {
        AttributeValues.Add(definition.DefaultValue.Clone());
    }

    public void RemoveAttribute(AttributeValue value)
    {
        AttributeValues.Remove(value);
    }
    public AttributeValue? GetAttributeByDefinitionId(int id)
    {
        return AttributeValues.FirstOrDefault(v => v.Definition.Id == id);
    }
}
