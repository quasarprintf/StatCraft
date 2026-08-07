using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Models.GameData.Attributes.FixedAttribute
{
    // A map attribute *definition*. Unlike BuildAttribute this is global — it exists once and applies to
    // every map — so it deliberately carries no owning map and no default value. A map's actual value
    // lives in a separate MapAttributeValue, and a freshly defined attribute is unset everywhere until
    // someone fills it in. Purely a marker over AttributeDefinition: every member it needs is shared.
    public class FixedAttribute : AttributeDefinition
    {
    }
}
