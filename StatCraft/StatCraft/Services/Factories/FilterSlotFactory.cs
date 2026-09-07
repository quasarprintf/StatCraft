using StatCraft.Models.GameData.Attributes;
using StatCraft.ViewModels.Windows.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.Factories;

public class FilterSlotFactory
{
    public FilterSlotViewModel CreateFromDefinition(AttributeDefinition attribute)
    {
        switch (attribute.Type)
        {
            case AttributeType.Bool:
                return new BoolFilterSlotViewModel(attribute.Name);
            case AttributeType.Values:
                var checkboxFilters = attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string>(o, o));
                return new CheckboxFilterSlotViewModel<string>(attribute.Name, checkboxFilters, showSearch: true);
            case AttributeType.Numeric:
            case AttributeType.Percent:
            default:
                return new NumericRangeFilterSlotViewModel(attribute.Name);
        }
    }
}
