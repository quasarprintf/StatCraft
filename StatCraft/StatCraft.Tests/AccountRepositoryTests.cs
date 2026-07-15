using StatCraft.Models.Battlenet;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class AccountRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AccountRepository _repository;

    public AccountRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _repository = new AccountRepository(_dbPath);
        _repository.Initialize();
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        _repository.Initialize();
    }

    [Fact]
    public void InsertAccount_ThenFindByAccountSub_ReturnsSameAccount()
    {
        BattleNetAccount account = new BattleNetAccount
        {
            BattleTag = "Maru#1234",
            AccountSub = "sub-1",
            EncryptedAccessToken = [1, 2, 3],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _repository.InsertAccount(account);
        BattleNetAccount? found = _repository.FindByAccountSub("sub-1");

        Assert.NotNull(found);
        Assert.Equal(account.Id, found!.Id);
        Assert.Equal("Maru#1234", found.BattleTag);
    }

    [Fact]
    public void FindByAccountSub_UnknownSub_ReturnsNull()
    {
        Assert.Null(_repository.FindByAccountSub("does-not-exist"));
    }

    [Fact]
    public void UpdateAccountTokens_UpdatesExistingRow()
    {
        BattleNetAccount account = new BattleNetAccount
        {
            BattleTag = "Old#1234",
            AccountSub = "sub-2",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _repository.InsertAccount(account);

        _repository.UpdateAccountTokens(account.Id, [9, 9], null, DateTimeOffset.UtcNow.AddHours(1), "New#5678");

        BattleNetAccount? updated = _repository.FindByAccountSub("sub-2");
        Assert.Equal("New#5678", updated!.BattleTag);
        Assert.Equal([9, 9], updated.EncryptedAccessToken);
    }

    [Fact]
    public void UpsertProfile_CalledTwiceForSameKey_UpdatesInPlaceInsteadOfDuplicating()
    {
        BattleNetAccount account = new BattleNetAccount
        {
            BattleTag = "Maru#1234",
            AccountSub = "sub-3",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _repository.InsertAccount(account);

        Sc2Profile profile = new Sc2Profile
        {
            BattleNetAccountId = account.Id,
            RegionId = "1",
            RealmId = "1",
            ProfileId = "12345",
            Name = "Maru",
        };
        _repository.UpsertProfile(profile);
        int firstId = profile.Id;

        profile.Name = "Maru2";
        _repository.UpsertProfile(profile);

        Assert.Equal(firstId, profile.Id);
        Sc2Profile saved = Assert.Single(_repository.GetAllProfiles());
        Assert.Equal("Maru2", saved.Name);
    }

    [Fact]
    public void GetAllProfiles_PopulatesAccountNavigationProperty()
    {
        BattleNetAccount account = new BattleNetAccount
        {
            BattleTag = "Maru#1234",
            AccountSub = "sub-4",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _repository.InsertAccount(account);

        Sc2Profile profile = new Sc2Profile
        {
            BattleNetAccountId = account.Id,
            RegionId = "2",
            RealmId = "1",
            ProfileId = "999",
            Name = "Serral",
        };
        _repository.UpsertProfile(profile);

        Sc2Profile loaded = Assert.Single(_repository.GetAllProfiles());
        Assert.NotNull(loaded.Account);
        Assert.Equal("Maru#1234", loaded.Account!.BattleTag);
    }

    [Fact]
    public void GetAllProfiles_SameAccountMultipleProfiles_ReturnsOneRowPerProfile()
    {
        BattleNetAccount account = new BattleNetAccount
        {
            BattleTag = "Maru#1234",
            AccountSub = "sub-5",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _repository.InsertAccount(account);

        _repository.UpsertProfile(new Sc2Profile { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = "1", Name = "MaruNA" });
        _repository.UpsertProfile(new Sc2Profile { BattleNetAccountId = account.Id, RegionId = "2", RealmId = "1", ProfileId = "1", Name = "MaruEU" });

        Assert.Equal(2, _repository.GetAllProfiles().Count);
    }

    [Fact]
    public void GetSetting_ReturnsNullWhenNotSet()
    {
        Assert.Null(_repository.GetSetting("MissingKey"));
    }

    [Fact]
    public void SetSetting_ThenGetSetting_RoundTrips()
    {
        _repository.SetSetting("ClientId", "abc123");
        Assert.Equal("abc123", _repository.GetSetting("ClientId"));
    }

    [Fact]
    public void SetSetting_CalledTwice_OverwritesValue()
    {
        _repository.SetSetting("ClientId", "first");
        _repository.SetSetting("ClientId", "second");
        Assert.Equal("second", _repository.GetSetting("ClientId"));
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
