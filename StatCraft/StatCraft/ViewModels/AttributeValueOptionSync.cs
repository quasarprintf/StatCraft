using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace StatCraft.ViewModels
{
    // Persists edits to an attribute definition's Values-type ValueOptions list. Shared by
    // BuildsPageViewModel and MapsPageViewModel: their BuildRepository/MapRepository both expose the same
    // InsertValueOption(int, string, int)/DeleteValueOption(int, string) shape, but the two repositories
    // don't share an interface, so the calls are passed in directly rather than inventing one just for
    // this.
    internal static class AttributeValueOptionSync
    {
        public static void Apply(NotifyCollectionChangedEventArgs e, int attributeId, IList<string> currentOptions,
            Action<int, string, int> insertOption, Action<int, string> deleteOption)
        {
            if (e.NewItems != null)
                foreach (string value in e.NewItems.OfType<string>())
                    insertOption(attributeId, value, currentOptions.IndexOf(value));

            if (e.OldItems != null)
                foreach (string value in e.OldItems.OfType<string>())
                    deleteOption(attributeId, value);
        }
    }
}
