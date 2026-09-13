using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace StatCraft.ViewModels.Windows.Filters;

public partial class AttributeFilterSlotViewModel<T> : WrappedFilterSlotViewModel<T> where T : IAttributedObject
{
    public AttributeFilterSlotViewModel(AttributeDefinition attribute)
    {
        WrappedFilter = CreateWrapped(attribute);
        base.BindWrapped();
    }

    private IFilterSlotViewModel<T> CreateWrapped(AttributeDefinition attribute)
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
