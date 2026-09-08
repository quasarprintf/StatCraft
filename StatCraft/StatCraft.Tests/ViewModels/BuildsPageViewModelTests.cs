using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.Factories;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Tests;

public class BuildsPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BuildRepository _buildRepo;
    private readonly BuildsPageViewModel _vm;

    public BuildsPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _buildRepo = new BuildRepository(_dbPath);
        _buildRepo.Initialize();
        MapRepository mapRepository = new(_dbPath);
        mapRepository.Initialize();
        GameDataRepository gameDataRepository = new(_dbPath);
        gameDataRepository.Initialize();
        AttributeRepository attributeRepository = new(_dbPath);
        attributeRepository.Initialize();

        _vm = new BuildsPageViewModel(_buildRepo, attributeRepository, gameDataRepository, new FilterSlotFactory())
        {
            PlayerRace = Race.Protoss,
        };
        _vm.AddBuildCommand.Execute(null);
    }

    // Pins BuildsPageViewModel's own contribution on top of BuildRepository.ChangeBuildDetailSortOrder
    // (covered separately in BuildRepositoryTests): the in-memory SelectedBuild.Details collection has
    // to end up reordered too, in lockstep with the DB write, since that's what the drag-drop UI is
    // actually bound to.
    [Fact]
    public void ChangeDetailIndex_ReordersSelectedBuildDetailsInMemoryAndPersists()
    {
        AddDetail("A");
        AddDetail("B");
        AddDetail("C");

        _vm.ChangeDetailIndex(sourceIndex: 2, targetIndex: 0);

        Assert.Equal(["C", "A", "B"], _vm.SelectedBuild!.Details.Select(d => d.Name));

        // Persisted, not just reordered in memory — a fresh load agrees.
        Assert.Equal(["C", "A", "B"], _buildRepo.GetBuildsForPlayerRace(Race.Protoss).Single().Details.Select(d => d.Name));
    }

    [Fact]
    public void ChangeDetailIndex_NoSelectedBuild_DoesNotThrow()
    {
        _vm.SelectedBuild = null;

        _vm.ChangeDetailIndex(0, 1);
    }

    private void AddDetail(string name)
    {
        _vm.AddDetailCommand.Execute(null);
        AttributeDefinition added = _vm.SelectedBuild!.Details[^1];
        added.Name = name;
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
        }
    }
}
