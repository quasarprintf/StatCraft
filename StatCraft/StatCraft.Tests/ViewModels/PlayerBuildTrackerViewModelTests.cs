using StatCraft.Models.GameData.Attributes;
using System.Collections.ObjectModel;
using Avalonia.Media;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels;
using AppColors = StatCraft.Styles.Colors;

namespace StatCraft.Tests;

public class PlayerBuildTrackerViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _gameDataRepository;
    private readonly BuildRepository _buildRepository;
    private readonly AccountRepository _accountRepository;
    private readonly MockLogger _logger = new();
    private readonly int _sc2ProfileId;

    public PlayerBuildTrackerViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _accountRepository = new AccountRepository(_dbPath);
        _accountRepository.Initialize();
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        // Before GameDataRepository, whose MapName -> MapId migration writes into the Maps table — the
        // same ordering App.axaml.cs enforces through DI.
        new MapRepository(_dbPath).Initialize();
        _gameDataRepository = new GameDataRepository(_dbPath);
        _gameDataRepository.Initialize();

        BattleNetAccount account = new()
        {
            BattleTag = "Player#1234",
            AccountSub = "sub-1",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _accountRepository.InsertAccount(account);
        Sc2Profile profile = new() { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = 111, Name = "Player" };
        _accountRepository.UpsertProfile(profile);
        _sc2ProfileId = profile.Id;
    }

    [Fact]
    public void RefreshAttributeEditors_AfterTemplateDefaultChanges_DoesNotChangeAlreadyLockedInValue()
    {
        BuildNode build = new() { Name = "4 Gate", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(build, null, 0);
        AttributeValue attr = new(new AttributeDefinition { Name = "Supply", Type = AttributeType.Numeric }) { NumericValue = 10 };
        _buildRepository.InsertAttribute(attr, build.Id, 0);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        // Select the build — this locks in the attribute's current default (10) as this player's own value.
        BuildNode loadedBuild = tree.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedBuild;
        Assert.Equal(10, Assert.Single(tracker.AttributeGroups.SelectMany(g => g.Attributes)).NumericValue);

        // Simulate editing the attribute's default on the Builds tab, then DataPageViewModel's
        // RefreshBuildTreeCache pattern of mutating the same tree collection in place.
        attr.NumericValue = 20;
        _buildRepository.UpdateAttribute(attr);
        tree.Clear();
        foreach (BuildNode node in _buildRepository.GetBuildsForPlayerRace(Race.Z))
            tree.Add(node);

        tracker.RefreshAttributeEditors();

        Assert.Equal(10, Assert.Single(tracker.AttributeGroups.SelectMany(g => g.Attributes)).NumericValue);
    }

    [Fact]
    public void SelectingABuild_ForTheFirstTime_UsesTemplatesCurrentDefault()
    {
        BuildNode build = new() { Name = "4 Gate", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(build, null, 0);
        AttributeValue attr = new(new AttributeDefinition { Name = "Supply", Type = AttributeType.Numeric }) { NumericValue = 10 };
        _buildRepository.InsertAttribute(attr, build.Id, 0);

        // Edit the default before anyone ever selects the build.
        attr.NumericValue = 20;
        _buildRepository.UpdateAttribute(attr);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        tracker.BuildSlots[0].SelectedBuildNode = tree.Single();

        Assert.Equal(20, Assert.Single(tracker.AttributeGroups.SelectMany(g => g.Attributes)).NumericValue);
    }

    // Reproduces the crash a real session hit: a build selected in a slot whose own BuildTree doesn't
    // (or no longer) contains it — e.g. a stale menu click racing a tree refresh. FindPath legitimately
    // returns null here; the slot must degrade to a blank label instead of throwing.
    [Fact]
    public void SelectingABuild_NotPresentInTheTree_DoesNotThrow()
    {
        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode offTreeBuild = new() { Id = 9999, Name = "Not In Tree" };
        tracker.BuildSlots[0].SelectedBuildNode = offTreeBuild;

        Assert.Equal("", tracker.BuildSlots[0].SelectedBuildLabel);
        Assert.Empty(tracker.AttributeGroups);
    }

    [Fact]
    public void AttributeGroups_MultiNodePath_OneGroupPerAttributeOwningNodeAtIncreasingDepth()
    {
        BuildNode root = new() { Name = "Cannon Rush", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(root, null, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "Chrono At Start", Type = AttributeType.Numeric }), root.Id, 0);

        BuildNode child = new() { Name = "Low Ground Start", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(child, root.Id, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "Contested Ramp", Type = AttributeType.Bool }), child.Id, 0);

        BuildNode grandchild = new() { Name = "Proxy Gate", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(grandchild, child.Id, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "Gate Count", Type = AttributeType.Numeric }), grandchild.Id, 0);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode loadedRoot = tree.Single();
        BuildNode loadedChild = loadedRoot.Children.Single();
        BuildNode loadedGrandchild = loadedChild.Children.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedGrandchild;

        Assert.Equal(3, tracker.AttributeGroups.Count);
        Assert.Equal(("Cannon Rush", 0), (tracker.AttributeGroups[0].BuildName, (int)(tracker.AttributeGroups[0].Margin.Left / 16)));
        Assert.Equal(("Low Ground Start", 1), (tracker.AttributeGroups[1].BuildName, (int)(tracker.AttributeGroups[1].Margin.Left / 16)));
        Assert.Equal(("Proxy Gate", 2), (tracker.AttributeGroups[2].BuildName, (int)(tracker.AttributeGroups[2].Margin.Left / 16)));
    }

    [Fact]
    public void AttributeGroups_NodeWithNoAttributes_ProducesNoGroupForThatNode()
    {
        BuildNode root = new() { Name = "Cannon Rush", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(root, null, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "Chrono At Start", Type = AttributeType.Numeric }), root.Id, 0);

        BuildNode childWithNoAttributes = new() { Name = "Proxy Forge", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(childWithNoAttributes, root.Id, 0);

        BuildNode grandchild = new() { Name = "Low Ground Start", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(grandchild, childWithNoAttributes.Id, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "Contested Ramp", Type = AttributeType.Bool }), grandchild.Id, 0);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode loadedRoot = tree.Single();
        BuildNode loadedChild = loadedRoot.Children.Single();
        BuildNode loadedGrandchild = loadedChild.Children.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedGrandchild;

        Assert.Equal(["Cannon Rush", "Low Ground Start"], tracker.AttributeGroups.Select(g => g.BuildName));
    }

    [Fact]
    public void AttributeGroups_TwoSlotsShareAnAncestor_ProducesOnlyOneGroupForIt()
    {
        BuildNode parent = new() { Name = "A", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(parent, null, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "SharedAttr", Type = AttributeType.Numeric }), parent.Id, 0);
        BuildNode childB = new() { Name = "B", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(childB, parent.Id, 0);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "BAttr", Type = AttributeType.Numeric }), childB.Id, 0);
        BuildNode childC = new() { Name = "C", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(childC, parent.Id, 1);
        _buildRepository.InsertAttribute(new AttributeValue(new AttributeDefinition { Name = "CAttr", Type = AttributeType.Numeric }), childC.Id, 0);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode loadedParent = tree.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedParent.Children.Single(n => n.Name == "B");
        tracker.BuildSlots[1].SelectedBuildNode = loadedParent.Children.Single(n => n.Name == "C");

        Assert.Equal(["A", "B", "C"], tracker.AttributeGroups.Select(g => g.BuildName));
    }

    [Fact]
    public void SelectedBuildsSummary_TwoBuildsShareCommonAncestor_CompactsTheSharedPrefix()
    {
        BuildNode parent = new() { Name = "A", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(parent, null, 0);
        BuildNode childB = new() { Name = "B", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(childB, parent.Id, 0);
        BuildNode childC = new() { Name = "C", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(childC, parent.Id, 1);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode loadedParent = tree.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedParent.Children.Single(n => n.Name == "B");
        tracker.BuildSlots[1].SelectedBuildNode = loadedParent.Children.Single(n => n.Name == "C");

        Assert.Equal("A > B, C", tracker.SelectedBuildsSummary);
    }

    // A > B > C, A > X > Y, and A > X > Z: the shared "A" collapses once, and X's own two children
    // collapse a second time one level deeper — the case a single top-level common-prefix check can't
    // express, since only two of the three builds share anything past "A".
    [Fact]
    public void SelectedBuildsSummary_BranchesAtMultipleDepths_CompactsEachSharedPrefix()
    {
        BuildNode a = new() { Name = "A", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(a, null, 0);
        BuildNode b = new() { Name = "B", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(b, a.Id, 0);
        BuildNode c = new() { Name = "C", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(c, b.Id, 0);
        BuildNode x = new() { Name = "X", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(x, a.Id, 1);
        BuildNode y = new() { Name = "Y", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(y, x.Id, 0);
        BuildNode z = new() { Name = "Z", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(z, x.Id, 1);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        BuildNode loadedA = tree.Single();
        BuildNode loadedB = loadedA.Children.Single(n => n.Name == "B");
        BuildNode loadedX = loadedA.Children.Single(n => n.Name == "X");

        tracker.BuildSlots[0].SelectedBuildNode = loadedB.Children.Single(n => n.Name == "C");
        tracker.BuildSlots[1].SelectedBuildNode = loadedX.Children.Single(n => n.Name == "Y");
        tracker.BuildSlots[2].SelectedBuildNode = loadedX.Children.Single(n => n.Name == "Z");

        Assert.Equal("A > B > C, X > Y, Z", tracker.SelectedBuildsSummary);
    }

    [Fact]
    public void SelectedBuildsSummary_BuildsWithNoCommonAncestor_ShowsBothFullPaths()
    {
        BuildNode rootA = new() { Name = "A", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(rootA, null, 0);
        BuildNode rootD = new() { Name = "D", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(rootD, null, 1);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree, _logger);

        tracker.BuildSlots[0].SelectedBuildNode = tree.Single(n => n.Name == "A");
        tracker.BuildSlots[1].SelectedBuildNode = tree.Single(n => n.Name == "D");

        Assert.Equal("A; D", tracker.SelectedBuildsSummary);
    }

    // The Data tab's build tabs are colored by each player's actual in-game color (see
    // GamePlayer.ColorArgb) rather than just ally/opponent side — when the color is already known
    // (a replay parsed since this was added), NameColor is set from it synchronously, no replay
    // re-read needed.
    [Fact]
    public void Construction_WithColorArgbAlreadySet_SetsNameColorFromIt()
    {
        GamePlayer player = new() { Name = "Ally", Clan = "", Mmr = 3000, Race = 'Z', Random = false, ColorArgb = unchecked((int)0xFFFF0000) };

        PlayerBuildTrackerViewModel tracker = new(player, _gameDataRepository, null, _logger);

        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(tracker.NameColor);
        Assert.Equal(Color.FromUInt32(unchecked((uint)0xFFFF0000)), brush.Color);
    }

    // No stored color and no way to look one up (no replayDataExtractor/replayPath given, as for the
    // session user's own tracker, which never shows a colored tab) — NameColor just stays unset rather
    // than throwing or leaving anything half-initialized.
    [Fact]
    public void Construction_WithNoColorArgbAndNoReplayToResolveFrom_LeavesNameColorNull()
    {
        GamePlayer player = new() { Name = "Ally", Clan = "", Mmr = 3000, Race = 'Z', Random = false };

        PlayerBuildTrackerViewModel tracker = new(player, _gameDataRepository, null, _logger);

        Assert.Null(tracker.NameColor);
    }

    // "Use Team Colors" overrides the replay-derived color entirely while on, regardless of whether
    // ColorArgb was already known.
    [Fact]
    public void Construction_WithUseTeamColorsOn_ColorsByAllyOpponentSideNotReplayColor()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 3000, Race = 'Z', Random = false, ColorArgb = unchecked((int)0xFFFF0000) };
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = 3000, Race = 'T', Random = false, ColorArgb = unchecked((int)0xFF00FF00) };

        PlayerBuildTrackerViewModel allyTracker = new(ally, _gameDataRepository, null, _logger, useTeamColors: true, isAlly: true);
        PlayerBuildTrackerViewModel opponentTracker = new(opponent, _gameDataRepository, null, _logger, useTeamColors: true, isAlly: false);

        Assert.Same(AppColors.AllyYellow, allyTracker.NameColor);
        Assert.Same(AppColors.OpponentRed, opponentTracker.NameColor);
    }

    // Toggling the setting back off (as DataPageViewModel does live via SetUseTeamColors when the
    // Settings tab checkbox changes) must restore the player's actual in-game color, not leave the team
    // color or go blank.
    [Fact]
    public void SetUseTeamColors_ToggledOffAfterOn_RestoresReplayColor()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 3000, Race = 'Z', Random = false, ColorArgb = unchecked((int)0xFFFF0000) };
        PlayerBuildTrackerViewModel tracker = new(ally, _gameDataRepository, null, _logger, useTeamColors: true, isAlly: true);
        Assert.Same(AppColors.AllyYellow, tracker.NameColor);

        tracker.SetUseTeamColors(false);

        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(tracker.NameColor);
        Assert.Equal(Color.FromUInt32(unchecked((uint)0xFFFF0000)), brush.Color);
    }

    private static GameData CreateGame()
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = Guid.NewGuid() + ".SC2Replay",
            ReplayTimestamp = DateTimeOffset.UtcNow,
            Win = 1m,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = 3000, Race = 'Z', Random = false },
            Allies = [],
            Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'T', Random = false }],
        };
        return new GameData { ReplayData = replay };
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
