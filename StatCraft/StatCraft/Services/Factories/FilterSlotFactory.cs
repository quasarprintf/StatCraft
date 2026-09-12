using StatCraft.Models.GameData.Attributes;
using StatCraft.ViewModels.Windows.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.Factories;

public class FilterSlotFactory
{
    public IFilterSlotViewModel<T> CreateFromDefinition<T>(AttributeDefinition attribute) where T : IAttributedObject
    {
        switch (attribute.Type)
        {
            case AttributeType.Bool:
                return new BoolFilterSlotViewModel<T>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.BoolValue);
            case AttributeType.Values:
                var checkboxFilters = attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string?>(o, o));
                return new CheckboxFilterSlotViewModel<T, string?>(attribute.Name, checkboxFilters, a => [a.GetAttributeByDefinitionId(attribute.Id)?.SelectedValue], showSearch: true);
            case AttributeType.Numeric:
                return new NumericRangeFilterSlotViewModel<T>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.NumericValue);
            case AttributeType.Percent:
                return new NumericRangeFilterSlotViewModel<T>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.PercentValue);
            default:
                throw new NotImplementedException();
        }
    }
}
