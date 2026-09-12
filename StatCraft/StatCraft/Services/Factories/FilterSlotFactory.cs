using StatCraft.Models.GameData.Attributes;
using StatCraft.ViewModels.Windows.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.Factories;

public class FilterSlotFactory
{
    public IFilterSlotViewModel CreateFromDefinition(AttributeDefinition attribute)
    {
        switch (attribute.Type)
        {
            case AttributeType.Bool:
                return new BoolFilterSlotViewModel<IAttributedObject>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.BoolValue);
            case AttributeType.Values:
                var checkboxFilters = attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string?>(o, o));
                return new CheckboxFilterSlotViewModel<IAttributedObject, string?>(attribute.Name, checkboxFilters, a => [a.GetAttributeByDefinitionId(attribute.Id)?.SelectedValue], showSearch: true);
            case AttributeType.Numeric:
                return new NumericRangeFilterSlotViewModel<IAttributedObject>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.NumericValue);
            case AttributeType.Percent:
                return new NumericRangeFilterSlotViewModel<IAttributedObject>(attribute.Name, a => a.GetAttributeByDefinitionId(attribute.Id)?.PercentValue);
            default:
                throw new NotImplementedException();
        }
    }
}
