using StatCraft.Models.GameData.Attributes;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.Models.GameData;

// One player's recorded build detail values — at most one per BuildDetail attribute of whichever builds
// they're tagged with. Each value carries its own definition (AttributeValue.Definition), which is what
// identifies the detail it belongs to.
public class BuildDetailValues : IAttributedObject
{
    public ObservableCollection<AttributeValue> AttributeValues { get; } = [];

    public void AddAttribute(AttributeValue value) 
    {
        AttributeValues.Add(value);
    }
    public void AddAttribute(AttributeDefinition definition) 
    {
        AttributeValues.Add(definition.DefaultValue.Clone());
    }
    public void RemoveAttribute(AttributeValue value)
    {
        AttributeValues.Remove(value);
    }
    public void RemoveAttribute(int definitionId)
    {
        for (int index = 0; index < AttributeValues.Count; index++)
        {
            if (AttributeValues[index].Definition.Id == definitionId)
            {
                AttributeValues.RemoveAt(index);
                break;
            }
        }
    }
    public AttributeValue? GetAttributeByDefinitionId(int id)
    {
        return AttributeValues.FirstOrDefault(v => v.Definition.Id == id);
    }
}
