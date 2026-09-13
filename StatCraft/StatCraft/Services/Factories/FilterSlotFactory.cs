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
        return new AttributeFilterSlotViewModel<T>(attribute);
    }
}
