using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.AttributeComponents
{
    public partial class AttributeValuesSelectViewModel : ViewModelBase
    {
        public ObservableCollection<AttributeDefinition> AllAttributes { get; } = [];
        public IEnumerable<AttributeDefinition> UnusedAttributes => Object == null ? Enumerable.Empty<AttributeDefinition>() : AllAttributes.Where(a => !Object.AttributeValues.Any(v => v.Definition.Id == a.Id));
        public IAttributedObject? Object 
        { 
            get => field;
            set
            {
                if (field != null)
                    UnWireObject(field);
                field = value;
                if (value != null)
                    WireObject(value);
                RaiseUnusedAttributesChanged();
            }
        }
        public bool HasUnusedAttributes => UnusedAttributes.Any();

        public AttributeValuesSelectViewModel(ObservableCollection<AttributeDefinition> attributes)
        {
            AllAttributes = attributes;
            AllAttributes.CollectionChanged += AllAttributesChanged;
        }

        #region property changed events
        private void AllAttributesChanged(object? sender, EventArgs e)
        {
            RaiseUnusedAttributesChanged();
        }
        private void WireObject(IAttributedObject target)
        {
            target.AttributeValues.CollectionChanged += AttributeValuesChanged;
        }
        private void UnWireObject(IAttributedObject target)
        {
            target.AttributeValues.CollectionChanged -= AttributeValuesChanged;
        }
        private void AttributeValuesChanged(object? sender, EventArgs e)
        {
            RaiseUnusedAttributesChanged();
        }
        private void RaiseUnusedAttributesChanged()
        {
            OnPropertyChanged(nameof(UnusedAttributes));
            OnPropertyChanged(nameof(HasUnusedAttributes));
        }
        #endregion

        #region commands
        [RelayCommand]
        public void AddAttribute(AttributeDefinition definition) 
        {
            Object?.AttributeValues.Add(definition.DefaultValue.Clone());
        }

        [RelayCommand]
        public void RemoveAttribute(AttributeValue value)
        {
            Object?.AttributeValues.Remove(value);
        }
        #endregion
    }
}
