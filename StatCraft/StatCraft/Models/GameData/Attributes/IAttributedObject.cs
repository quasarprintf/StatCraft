using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace StatCraft.Models.GameData.Attributes
{
    public interface IAttributedObject
    {
        ObservableCollection<AttributeValue> AttributeValues { get; }
        void AddAttribute(AttributeDefinition definition);
        void RemoveAttribute(AttributeValue value);
    }
}
