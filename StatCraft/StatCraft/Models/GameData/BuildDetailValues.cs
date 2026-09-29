using StatCraft.Models.GameData.Attributes;
using System.Collections.Generic;

namespace StatCraft.Models.GameData;

// One player's recorded build detail values — at most one per BuildDetail attribute of whichever builds
// they're tagged with. Each value carries its own definition (AttributeValue.Definition), which is what
// identifies the detail it belongs to.
public class BuildDetailValues
{
    public List<AttributeValue> Values { get; set; } = [];
}
