using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData.Attributes.FixedAttribute;

namespace StatCraft.Models.GameData.Maps
{
    // A map a game was played on. Created either by playing on it (ReplayImportService resolves the
    // replay's map name through MapRepository.GetOrCreateMap) or by hand on the Maps tab.
    //
    // Public rather than internal, matching BuildNode, because MapsPageViewModel exposes maps publicly.
    // Maps never nest — there is no parent or children, unlike BuildNode.
    public partial class Map : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;

        // One entry per globally defined MapAttribute, including ones this map has no value for —
        // MapsPageViewModel keeps this aligned with the definition list so the editor can always show
        // every attribute, set or not.
        public ObservableCollection<FixedAttributeValue> AttributeValues { get; } = [];
    }
}
