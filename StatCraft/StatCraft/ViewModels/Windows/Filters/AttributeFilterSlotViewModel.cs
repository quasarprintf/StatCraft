using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.SequentialFilters;

namespace StatCraft.ViewModels.Windows.Filters;

public partial class AttributeFilterSlotViewModel<T> : WrappedFilterSlotViewModel<T>, IFilterSlotViewModel<T> where T : IAttributedObject
{
    private IFilterSlotViewModel<AttributeValue> _wrappedFilter => (IFilterSlotViewModel<AttributeValue>)WrappedFilter;
    private AttributeDefinition _attribute { get; set; }
    public AttributeFilterSlotViewModel(AttributeDefinition attribute)
    {
        _attribute = attribute;
        WrappedFilter = CreateWrapped(attribute);
        base.BindWrapped();
    }

    private IFilterSlotViewModel<AttributeValue> CreateWrapped(AttributeDefinition attribute)
    {
        switch (attribute.Type)
        {
            case AttributeType.Bool:
                return new BoolFilterSlotViewModel<AttributeValue>(attribute.Name, a => a?.BoolValue);
            case AttributeType.Values:
                var checkboxFilters = attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string?>(o, o));
                return new CheckboxFilterSlotViewModel<AttributeValue, string?>(attribute.Name, checkboxFilters, a => [a?.SelectedValue], showSearch: true);
            case AttributeType.Numeric:
                return new NumericRangeFilterSlotViewModel<AttributeValue>(attribute.Name, a => a?.NumericValue);
            case AttributeType.Percent:
                return new NumericRangeFilterSlotViewModel<AttributeValue>(attribute.Name, a => a?.PercentValue);
            default:
                throw new NotImplementedException();
        }
    }

    public IFilter<T> GetFilter()
    {
        IFilter<AttributeValue> filter = _wrappedFilter.GetFilter();
        SequentialAllFilter<T, AttributeValue> wrapper = new SequentialAllFilter<T, AttributeValue>(filter, m => [m.GetAttributeByDefinitionId(_attribute.Id)]);
        return wrapper;
    }
}
