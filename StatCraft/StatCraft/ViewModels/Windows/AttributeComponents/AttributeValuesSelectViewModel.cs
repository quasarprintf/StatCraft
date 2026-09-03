using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.AttributeComponents
{
    public partial class AttributeValuesSelectViewModel : ViewModelBase
    {
        public event EventHandler<AttributeValue>? ValueChanged;
        public event EventHandler<AttributeValue>? ValueDeleted;

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
            foreach (AttributeValue value in target.AttributeValues)
                WireValue(target, value);
        }
        private void UnWireObject(IAttributedObject target)
        {
            target.AttributeValues.CollectionChanged -= AttributeValuesChanged;
            foreach (AttributeValue value in target.AttributeValues)
                UnWireValue(target, value);
        }
        private void AttributeValuesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (Object == null)
                return;
            if (e.OldItems != null)
            {
                foreach (AttributeValue value in e.OldItems.OfType<AttributeValue>())
                {
                    UnWireValue(Object, value);
                    ValueDeleted?.Invoke(Object, value);
                }
            }
            if (e.NewItems != null)
            {
                foreach (AttributeValue value in e.NewItems.OfType<AttributeValue>())
                {
                    WireValue(Object, value);
                    ValueChanged?.Invoke(Object, value);
                }
            }

            RaiseUnusedAttributesChanged();
        }
        private void RaiseUnusedAttributesChanged()
        {
            OnPropertyChanged(nameof(UnusedAttributes));
            OnPropertyChanged(nameof(HasUnusedAttributes));
        }

        private void ValuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AttributeValue.HasValue))
                return;
            if (Object == null)
                return;
            if (s is not AttributeValue value)
                return;

            ValueChanged?.Invoke(Object, value);
        }
        private void UnWireValue(IAttributedObject target, AttributeValue value)
        {
            value.PropertyChanged -= ValuePropertyChanged;
        }
        private void WireValue(IAttributedObject target, AttributeValue value)
        {
            value.PropertyChanged += ValuePropertyChanged;
        }
        #endregion

        #region commands
        [RelayCommand]
        public void AddAttribute(AttributeDefinition definition) 
        {
            Object?.AddAttribute(definition);
        }

        [RelayCommand]
        public void RemoveAttribute(AttributeValue value)
        {
            Object?.RemoveAttribute(value);
        }
        #endregion
    }
}
