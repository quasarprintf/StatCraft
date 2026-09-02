using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows.DataComponents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows
{
    public partial class RaceOption(Race value) : ObservableObject
    {
        public string Display => Value.Display();
        public Race Value { get; } = value;

        [ObservableProperty] private bool _isSelected;
    }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        // Raised instead of deleting immediately when the build (or a descendant, since deleting a
        // parent cascades its whole subtree) has games recorded against it. The view shows a
        // confirmation dialog and, if accepted, calls ConfirmDeleteBuild.
        public event Action<BuildNode>? DeleteConfirmationRequested;

        private readonly BuildRepository _buildRepo;
        private readonly AttributeRepository _attributeRepo;
        private readonly GameDataRepository _gameDataRepo;
        private readonly HashSet<Race> _loadedPlayerRaces = [];

        [NotifyPropertyChangedFor(nameof(Builds))]
        [ObservableProperty] private Race _playerRace = Race.Zerg;

        [ObservableProperty] private BuildNode? _selectedBuild;

        public IReadOnlyList<RaceOption> PlayerRaceOptions { get; }
        public IReadOnlyList<RaceOption> OpponentRaceOptions { get; }

        private readonly Dictionary<Race, ObservableCollection<BuildNode>> _buildsByPlayerRace =
            Enum.GetValues<Race>().ToDictionary(r => r, _ => new ObservableCollection<BuildNode>());
        public ObservableCollection<BuildNode> Builds => _buildsByPlayerRace[PlayerRace];

        public ObservableCollection<AttributeDefinition> AllAttributes { get; } = [];
        public IEnumerable<AttributeDefinition> UnusedAttributes => SelectedBuild == null ? Enumerable.Empty<AttributeDefinition>() : AllAttributes.Where(a => !SelectedBuild.StaticAttributes.Any(v => v.Definition.Id == a.Id));
        public bool HasUnusedAttributes => UnusedAttributes.Any();

        public BuildsPageViewModel(BuildRepository buildRepository, AttributeRepository attributeRepository, GameDataRepository gameDataRepository)
        {
            _buildRepo = buildRepository;
            _attributeRepo = attributeRepository;
            _gameDataRepo = gameDataRepository;
            PlayerRaceOptions = Enum.GetValues<Race>()
                .Select(r => new RaceOption(r) { IsSelected = r == PlayerRace })
                .ToList();
            OpponentRaceOptions = Enum.GetValues<Race>()
                .Select(r => new RaceOption(r))
                .ToList();

            // Must happen before LoadPlayerRaceIfNeeded below — it passes AllAttributes straight through
            // to GetBuildsForPlayerRace so the very first load's static attributes (including the
            // mandatory-on-root backfill) aren't silently skipped by loading against an empty list.
            foreach (AttributeDefinition attribute in _attributeRepo.GetAllAttributes(AttributeScope.Build))
                AllAttributes.Add(attribute);
            //TODO:
            //foreach (AttributeDefinition attribute in AllAttributes)
                //AddFilterSlot(attribute);
            //ApplyFilters();

            LoadPlayerRaceIfNeeded(PlayerRace);
            RefreshOpponentFilter();

            _attributeRepo.AttributesChanged += SyncAttributesFromRepository;

            AllAttributes.CollectionChanged += RaiseUnusedAttributesChanged;
        }

        [RelayCommand]
        public void SelectPlayerRace(Race race)
        {
            PlayerRace = race;
            foreach (RaceOption option in PlayerRaceOptions)
                option.IsSelected = option.Value == race;
            LoadPlayerRaceIfNeeded(race);
            RefreshOpponentFilter();
            SelectFirstBuild();
        }

        [RelayCommand]
        public void ToggleOpponentRace(Race race)
        {
            RaceOption? option = OpponentRaceOptions.FirstOrDefault(o => o.Value == race);
            if (option == null) return;

            option.IsSelected = !option.IsSelected;
            AfterOpponentFilterChanged();
        }

        [RelayCommand]
        public void SelectOpponentRaceExclusive(Race race)
        {
            foreach (RaceOption option in OpponentRaceOptions)
                option.IsSelected = option.Value == race;
            AfterOpponentFilterChanged();
        }

        private void AfterOpponentFilterChanged()
        {
            RefreshOpponentFilter();
            if (SelectedBuild == null || !SelectedBuild.MatchesOpponentFilter)
                SelectFirstBuild();
        }

        // Recomputes BuildNode.MatchesOpponentFilter (which drives the TreeView's per-item visibility)
        // for every currently-loaded build under the current PlayerRace, based on the union of every
        // currently-selected opponent race — a build matches if it supports at least one of them. The
        // child-⊆-parent matchup invariant still guarantees a non-matching node never has a matching
        // descendant under this OR'd filter (any bit a child has, its parent has too), so filtering
        // never orphans a visible child under a hidden parent.
        private void RefreshOpponentFilter()
        {
            Matchups flags = OpponentRaceOptions.Where(o => o.IsSelected)
                .Aggregate(Matchups.None, (acc, o) => acc | ToMatchupFlag(o.Value));
            foreach (BuildNode root in Builds)
                RefreshOpponentFilter(root, flags);
        }

        private static void RefreshOpponentFilter(BuildNode node, Matchups flags)
        {
            node.MatchesOpponentFilter = node.Matchups == Matchups.None || (node.Matchups & flags) != Matchups.None;
            foreach (BuildNode child in node.Children)
                RefreshOpponentFilter(child, flags);
        }

        private static Matchups ToMatchupFlag(Race race) => race switch
        {
            Race.Zerg => Matchups.VsZ,
            Race.Terran => Matchups.VsT,
            Race.Protoss => Matchups.VsP,
            _ => Matchups.None,
        };

        private void LoadPlayerRaceIfNeeded(Race playerRace)
        {
            if (!_loadedPlayerRaces.Add(playerRace)) return;
            foreach (BuildNode node in _buildRepo.GetBuildsForPlayerRace(playerRace, AllAttributes))
            {
                _buildsByPlayerRace[playerRace].Add(node);
            }
        }

        private void SyncAttributesFromRepository()
        {
            List<AttributeDefinition> dbAttributes = _attributeRepo.GetAllAttributes(AttributeScope.Build);
            Dictionary<int, AttributeDefinition> dbById = dbAttributes.ToDictionary(a => a.Id);

            //sync deleted attributes
            foreach (AttributeDefinition cachedAttr in AllAttributes.Where(a => !dbById.ContainsKey(a.Id)).ToList())
            {
                AllAttributes.Remove(cachedAttr);

                foreach (BuildNode rootNode in _buildsByPlayerRace.SelectMany(r => r.Value))
                {
                    RemoveAttributeRecursively(rootNode, cachedAttr);
                }

                //TODO:
                //RemoveFilterSlot(cachedAttr);
            }

            //sync edited attributes
            foreach (AttributeDefinition cachedAttr in AllAttributes)
            {
                AttributeDefinition dbAttr = dbById[cachedAttr.Id];

                if (cachedAttr.Name != dbAttr.Name)
                {
                    cachedAttr.Name = dbAttr.Name;
                    //TODO:
                    //if (_slotByAttribute.TryGetValue(cachedAttr, out FilterSlotViewModel? slot))
                    //    slot.Title = dbAttr.Name;
                }

                if (cachedAttr.Type != dbAttr.Type)
                {
                    cachedAttr.Type = dbAttr.Type;
                    // Numeric/Percent vs. Bool vs. Values are different FilterSlotViewModel subclasses,
                    // so the slot itself has to be replaced rather than patched — but only for this one
                    // attribute, and preserving whether it was actually showing.
                    //TODO:
                    //bool wasVisible = _slotByAttribute.TryGetValue(cachedAttr, out FilterSlotViewModel? old) && old.IsVisible;
                    //RemoveFilterSlot(cachedAttr);
                    //AddFilterSlot(cachedAttr, wasVisible);
                }

                if (dbAttr.IsMandatory != cachedAttr.IsMandatory)
                {
                    cachedAttr.IsMandatory = dbAttr.IsMandatory;
                    List<BuildNode> nodesToSave = new List<BuildNode>();

                    // Children can override parent, but don't have to, so only roots are updated for mandatory toggle
                    if (dbAttr.IsMandatory)
                    {
                        foreach (BuildNode rootNode in _buildsByPlayerRace.SelectMany(r => r.Value))
                        {
                            if (!rootNode.StaticAttributes.Any(a => a.Definition.Id == dbAttr.Id))
                            {
                                rootNode.StaticAttributes.Add(cachedAttr.DefaultValue.Clone());
                                nodesToSave.Add(rootNode);
                            }
                        }
                    }
                    else
                    {
                        foreach (BuildNode rootNode in _buildsByPlayerRace.SelectMany(r => r.Value))
                        {
                            AttributeValue? value = rootNode.StaticAttributes.FirstOrDefault(v => v.Definition.Id == cachedAttr.Id);
                            if (value != null && !value.HasValue)
                            {
                                rootNode.StaticAttributes.Remove(value);
                                nodesToSave.Add(rootNode);
                            }
                        }
                    }
                    _buildRepo.SaveStaticAttributes(nodesToSave, dbAttr.Id);
                }

                SyncValueOptions(cachedAttr, dbAttr.ValueOptions);

                if (dbAttr.DefaultValue.HasValue)
                    cachedAttr.DefaultValue.ApplyStoredValue(dbAttr.DefaultValue.Serialize()!);
                else
                    cachedAttr.DefaultValue.Clear();
            }

            //sync new attributes
            HashSet<int> knownIds = AllAttributes.Select(a => a.Id).ToHashSet();
            foreach (AttributeDefinition dbAttr in dbAttributes.Where(a => !knownIds.Contains(a.Id)))
            {
                AllAttributes.Add(dbAttr);

                if (dbAttr.IsMandatory)
                {
                    // Defined for every map at once, and unset on all of them until someone fills it in.
                    List<BuildNode> nodesToSave = new List<BuildNode>();
                    // Children can override parent, but don't have to, so only roots are updated for mandatory toggle
                    foreach (BuildNode rootNode in _buildsByPlayerRace.SelectMany(r => r.Value))
                    {
                        rootNode.StaticAttributes.Add(dbAttr.DefaultValue.Clone());
                        nodesToSave.Add(rootNode);
                    }
                    _buildRepo.SaveStaticAttributes(nodesToSave, dbAttr.Id);
                }

                //TODO:
                //AddFilterSlot(dbAttr);
            }

            //TODO:
            //ApplyFilters();
        }
        private void SyncValueOptions(AttributeDefinition attribute, ObservableCollection<string> latest)
        {
            bool changed = false;

            //remove deleted options
            foreach (string stale in attribute.ValueOptions.Where(o => !latest.Contains(o)).ToList())
            {
                attribute.ValueOptions.Remove(stale);
                changed = true;
            }

            //sync new options
            foreach (string value in latest.Where(o => !attribute.ValueOptions.Contains(o)))
            {
                attribute.ValueOptions.Add(value);
                changed = true;
            }

            if (!changed)
                return;

            //TODO:
            //if (_slotByAttribute.TryGetValue(attribute, out FilterSlotViewModel? slot) &&
            //    slot is CheckboxFilterSlotViewModel<string> stringSlot)
            //{
            //    HashSet<string> previouslyChecked = stringSlot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
            //    stringSlot.ReplaceOptions(attribute.ValueOptions
            //        .Select(o => new CheckboxFilterOptionViewModel<string>(o, o) { IsChecked = previouslyChecked.Contains(o) }));
            //}
        }

        private void RemoveAttributeRecursively(BuildNode node, AttributeDefinition attribute)
        {
            AttributeValue? value = node.StaticAttributes.FirstOrDefault(v => v.Definition.Id == attribute.Id);
            if (value != null)
                node.StaticAttributes.Remove(value);
            foreach (var child in node.Children)
                RemoveAttributeRecursively(child, attribute);
        }

        partial void OnSelectedBuildChanging(BuildNode? value)
        {
            if (SelectedBuild != null)
                UnWireNode(SelectedBuild);
            if (value != null)
                WireNode(value);
        }

        partial void OnSelectedBuildChanged(BuildNode? value)
        {
            OnPropertyChanged(nameof(UnusedAttributes));
            OnPropertyChanged(nameof(HasUnusedAttributes));
        }

        private void RaiseUnusedAttributesChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(UnusedAttributes));
            OnPropertyChanged(nameof(HasUnusedAttributes));
        }

        private void WireNode(BuildNode node)
        {
            node.StaticAttributes.CollectionChanged += RaiseUnusedAttributesChanged;
            node.StaticAttributes.CollectionChanged += StaticAttributeValuesChanged;
            node.PropertyChanged += NodePropertyChanged;
            foreach (AttributeDefinition attr in node.Details)
                WireDetail(attr);
            foreach (AttributeValue attr in node.StaticAttributes)
                WireStaticValue(node, attr);
        }
        private void UnWireNode(BuildNode node)
        {
            node.StaticAttributes.CollectionChanged -= RaiseUnusedAttributesChanged;
            node.StaticAttributes.CollectionChanged -= StaticAttributeValuesChanged;
            node.PropertyChanged -= NodePropertyChanged;
            foreach (AttributeDefinition attr in node.Details)
                UnWireDetail(attr);
            foreach (AttributeValue attr in node.StaticAttributes)
                UnWireStaticValue(node, attr);
        }

        private void NodePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is BuildNode n && (e.PropertyName == nameof(BuildNode.Name) || e.PropertyName == nameof(BuildNode.Description)
                    || e.PropertyName == nameof(BuildNode.Matchups)))
                    _buildRepo.UpdateBuild(n);
        }
        private void StaticAttributeValuesChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (SelectedBuild == null)
                return;
            if (e.OldItems != null)
            {
                foreach (AttributeValue value in e.OldItems.OfType<AttributeValue>())
                {
                    UnWireStaticValue(SelectedBuild, value);
                    _buildRepo.SaveStaticAttribute(SelectedBuild.Id, value.Definition.Id, null);
                }
            }
            if (e.NewItems != null)
            {
                foreach (AttributeValue value in e.NewItems.OfType<AttributeValue>())
                {
                    WireStaticValue(SelectedBuild, value);
                    _buildRepo.SaveStaticAttribute(SelectedBuild.Id, value.Definition.Id, value.Serialize());
                }
            }
        }
        private void UnWireStaticValue(BuildNode map, AttributeValue value)
        {
            value.PropertyChanged -= ValuePropertyChanged;
        }
        private void WireStaticValue(BuildNode map, AttributeValue value)
        {
            value.PropertyChanged += ValuePropertyChanged;
        }
        private void ValuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AttributeValue.HasValue))
                return;
            if (SelectedBuild == null)
                return;
            if (s is not AttributeValue value)
                return;

            // Serialize() returns null when unset, and SaveValue deletes the row for null — that
            // absence is what "no value" actually is in the database.
            _buildRepo.SaveStaticAttribute(SelectedBuild.Id, value.Definition.Id, value.Serialize());
            //TODO:
            //ApplyFilters();
        }

        private void WireDetail(AttributeDefinition attr)
        {
            attr.DefinitionChanged += DetailDefinitionPropertyChanged;
            attr.DefaultValue.ValueChanged += DetailPropertyChanged;
            attr.ValueOptionsChanged += DetailDefinitionOptionsChanged;
        }
        private void UnWireDetail(AttributeDefinition attr)
        {
            attr.DefinitionChanged -= DetailDefinitionPropertyChanged;
            attr.DefaultValue.ValueChanged -= DetailPropertyChanged;
            attr.ValueOptionsChanged -= DetailDefinitionOptionsChanged;
        }
        private void DetailDefinitionPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not AttributeDefinition definition)
                return;
            if (e.PropertyName == nameof(AttributeDefinition.Name) || e.PropertyName == nameof(AttributeDefinition.Type))
                    _buildRepo.UpdateAttribute(definition.DefaultValue);
        }
        private void DetailPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is not AttributeValue attr)
                return;
            if (e.PropertyName == nameof(AttributeValue.NumericValue) || e.PropertyName == nameof(AttributeValue.BoolValue)
                    || e.PropertyName == nameof(AttributeValue.PercentValue) || e.PropertyName == nameof(AttributeValue.SelectedValue))
                    _buildRepo.UpdateAttribute(attr);
        }
        private void DetailDefinitionOptionsChanged(object? s, CollectionChangeEventArgs e)
        {
            if (s is not AttributeDefinition definition)
                return;
            switch (e.Action)
            {
                case CollectionChangeAction.Add:
                    _buildRepo.InsertValueOption(definition.Id, (string)e.Element!);
                    return;
                case CollectionChangeAction.Remove:
                    _buildRepo.DeleteValueOption(definition.Id, (string)e.Element!);
                    return;
            }
        }

        public void SelectFirstBuild()
        {
            SelectedBuild = Builds.FirstOrDefault(n => n.MatchesOpponentFilter);
        }

        [RelayCommand]
        public void AddBuild()
        {
            BuildNode node = new BuildNode { Name = "New Build", PlayerRace = PlayerRace, Matchups = Matchups.VsZ | Matchups.VsT | Matchups.VsP };
            _buildRepo.InsertBuild(node, null, Builds.Count);
            Builds.Add(node);
            RefreshOpponentFilter();
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddChildBuild(BuildNode parent)
        {
            BuildNode node = new BuildNode { Name = "New Build", PlayerRace = parent.PlayerRace, Matchups = parent.Matchups };
            _buildRepo.InsertBuild(node, parent.Id, parent.Children.Count);
            parent.Children.Add(node);
            parent.IsExpanded = true;
            RefreshOpponentFilter();
            SelectedBuild = node;
        }

        [RelayCommand]
        public void ToggleMatchup(Race opponentRace)
        {
            if (SelectedBuild == null) return;

            Matchups flag = ToMatchupFlag(opponentRace);
            bool turningOn = !SelectedBuild.Matchups.HasFlag(flag);

            if (turningOn)
            {
                BuildNode? parent = FindParent(Builds, SelectedBuild);
                if (parent != null && !parent.Matchups.HasFlag(flag))
                    return;
                SelectedBuild.Matchups |= flag;
            }
            else
            {
                SelectedBuild.Matchups &= ~flag;
            }

            CascadeMatchupToDescendants(SelectedBuild, flag, turningOn);
            RefreshOpponentFilter();
        }

        private static void CascadeMatchupToDescendants(BuildNode node, Matchups flag, bool turningOn)
        {
            foreach (BuildNode child in node.Children)
            {
                child.Matchups = turningOn ? child.Matchups | flag : child.Matchups & ~flag;
                CascadeMatchupToDescendants(child, flag, turningOn);
            }
        }

        [RelayCommand]
        public void DeleteBuild(BuildNode node)
        {
            if (_gameDataRepo.IsAnyBuildReferenced(CollectSubtreeIds(node)))
            {
                DeleteConfirmationRequested?.Invoke(node);
                return;
            }

            PerformDelete(node);
        }

        public void ConfirmDeleteBuild(BuildNode node)
        {
            PerformDelete(node);
        }

        private void PerformDelete(BuildNode node)
        {
            bool needsReselect = SelectedBuild == node || (SelectedBuild != null && ContainsDescendant(node, SelectedBuild));
            BuildNode? replacement = needsReselect ? FindReplacementSelection(node) : null;

            _buildRepo.DeleteBuild(node.Id);
            RemoveNode(Builds, node);

            if (needsReselect)
                SelectedBuild = replacement;
        }

        private static IEnumerable<int> CollectSubtreeIds(BuildNode node)
        {
            yield return node.Id;
            foreach (BuildNode child in node.Children)
                foreach (int id in CollectSubtreeIds(child))
                    yield return id;
        }

        private BuildNode? FindReplacementSelection(BuildNode node)
        {
            BuildNode? parent = FindParent(Builds, node);
            if (parent != null) return parent;

            int index = Builds.IndexOf(node);
            if (index > 0) return Builds[index - 1];

            return Builds.Count > 1 ? Builds[1] : null;
        }

        private static BuildNode? FindParent(ObservableCollection<BuildNode> nodes, BuildNode target)
        {
            foreach (BuildNode n in nodes)
            {
                if (n.Children.Contains(target)) return n;
                BuildNode? found = FindParent(n.Children, target);
                if (found != null) return found;
            }
            return null;
        }

        private static bool RemoveNode(ObservableCollection<BuildNode> nodes, BuildNode target)
        {
            if (nodes.Remove(target)) return true;
            foreach (BuildNode node in nodes)
                if (RemoveNode(node.Children, target)) return true;
            return false;
        }

        private static bool ContainsDescendant(BuildNode root, BuildNode target)
        {
            foreach (BuildNode child in root.Children)
                if (child == target || ContainsDescendant(child, target)) return true;
            return false;
        }

        [RelayCommand]
        public void AddDetail()
        {
            if (SelectedBuild == null) return;
            AttributeDefinition definition = new AttributeDefinition(AttributeScope.BuildDetail);
            AttributeValue attr = definition.DefaultValue;
            _buildRepo.InsertAttribute(attr, SelectedBuild.Id, SelectedBuild.Details.Count);
            WireDetail(definition);
            SelectedBuild.Details.Add(definition);
        }

        [RelayCommand]
        public void RemoveDetail(AttributeDefinition attribute)
        {
            if (SelectedBuild == null) return;
            _buildRepo.DeleteAttribute(attribute.Id);
            UnWireDetail(attribute);
            SelectedBuild.Details.Remove(attribute);
        }
    }
}
