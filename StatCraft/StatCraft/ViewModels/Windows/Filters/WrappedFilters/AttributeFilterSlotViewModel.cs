using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.SequentialFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters.WrappedFilters;

public partial class AttributeFilterSlotViewModel<T> : WrappedFilterSlotViewModel<T>, IFilterSlotViewModel<T> where T : IAttributedObject
{
    private IFilterSlotViewModel<AttributeValue> _wrappedFilter => (IFilterSlotViewModel<AttributeValue>)WrappedFilter;
    public AttributeDefinition Attribute { get; }
    public AttributeFilterSlotViewModel(AttributeDefinition attribute)
    {
        Attribute = attribute;
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
                return new CheckboxFilterSlotViewModel<AttributeValue, string?>(attribute.Name, checkboxFilters, a => a?.SelectedValue == null ? [] : [a.SelectedValue], showSearch: true);
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
        SequentialAllFilter<T, AttributeValue> wrapper = new SequentialAllFilter<T, AttributeValue>(filter, m => [m.GetAttributeByDefinitionId(Attribute.Id)]);
        return wrapper;
    }

    public void Refresh()
    {
        if (WrappedFilter is CheckboxFilterSlotViewModel<AttributeValue, string> stringSlot)
        {
            HashSet<string> previouslyChecked = stringSlot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
            stringSlot.ReplaceOptions(Attribute.ValueOptions
                .Select(o => new CheckboxFilterOptionViewModel<string>(o, o) { IsChecked = previouslyChecked.Contains(o) }));
        }
    }
}
