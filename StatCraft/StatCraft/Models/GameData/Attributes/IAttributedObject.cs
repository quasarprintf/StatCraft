using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace StatCraft.Models.GameData.Attributes
{
    public interface IAttributedObject
    {
        public ObservableCollection<AttributeValue> AttributeValues { get; }
    }
}
