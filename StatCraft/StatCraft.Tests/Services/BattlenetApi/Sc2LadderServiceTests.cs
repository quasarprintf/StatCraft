using System.Net;
using System.Net.Http;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BattlenetApi;
using StatCraft.Tests.Mocks;

namespace StatCraft.Tests;

// JSON fixtures below preserve the exact property names and nesting of real Battle.net API responses,
// which is what the service has to parse — but every profile id and display name in them is synthetic,
// so the suite isn't pinned to any one real account.
public class Sc2LadderServiceTests
{
    private const string SummaryWithOne1v1Ladder = """
        {
          "showCaseEntries": [],
          "placementMatches": [],
          "allLadderMemberships": [
            { "ladderId": "271205", "localizedGameMode": "1v1 Master", "rank": 9 }
          ]
        }
        """;

    private const string LadderWithSelfProtoss = """
        {
          "ladderTeams": [
            {
              "teamMembers": [
                { "id": "999999", "realm": 1, "region": 2, "displayName": "SomeoneElse", "favoriteRace": "zerg" }
              ],
              "points": 416, "wins": 10, "losses": 8, "mmr": 5739
            },
            {
              "teamMembers": [
                { "id": "1234567", "realm": 1, "region": 2, "displayName": "TestPlayer", "favoriteRace": "protoss" }
              ],
              "points": 350, "wins": 9, "losses": 8, "mmr": 5239
            }
          ]
        }
        """;

    private const string RandomLadder = """
        {
          "ladderTeams": [
            { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 2, "favoriteRace": "random" } ], "mmr": 3900 }
          ]
        }
        """;

    private static Sc2Profile EuProfile => new() { RegionId = "2", RealmId = "1", ProfileId = 1234567, Name = "TestPlayer" };

    [Fact]
    public async Task GetCurrentMmr_MatchingProfileAndRace_ReturnsThatTeamsMmr()
    {
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, LadderWithSelfProtoss);

        long? mmr = await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None);

        Assert.Equal(5239, mmr);
    }

    [Fact]
    public async Task GetCurrentMmr_IgnoresOtherPlayersTeams()
    {
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, LadderWithSelfProtoss);

        long? mmr = await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None);

        // 5739 is another player's team in the same ladder and must never be picked up.
        Assert.NotEqual(5739, mmr);
    }

    [Fact]
    public async Task GetCurrentMmr_ProfileNotInLadder_ReturnsNull()
    {
        Sc2Profile stranger = new() { RegionId = "2", RealmId = "1", ProfileId = 12345, Name = "nobody" };
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, LadderWithSelfProtoss);

        Assert.Null(await service.GetCurrentMmrAsync(stranger, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_SameIdDifferentRegion_DoesNotMatch()
    {
        // profileId alone isn't unique — it's only meaningful together with realm and region.
        Sc2Profile otherRegion = new() { RegionId = "1", RealmId = "1", ProfileId = 1234567, Name = "TestPlayer" };
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, LadderWithSelfProtoss);

        Assert.Null(await service.GetCurrentMmrAsync(otherRegion, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_RaceDoesNotExist_ReturnsNull()
    {
        // A Random game's replay records the spawned race, which needn't match the queued ladder race.
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, LadderWithSelfProtoss);

        Assert.Null(await service.GetCurrentMmrAsync(EuProfile, LadderRace.Z, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_PrefersExactRaceMatchOverOtherLadder()
    {
        const string twoLadders = """
            {
              "allLadderMemberships": [
                { "ladderId": "1", "localizedGameMode": "1v1 Master", "rank": 1 },
                { "ladderId": "2", "localizedGameMode": "1v1 Diamond", "rank": 2 }
              ]
            }
            """;
        const string zergLadder = """
            {
              "ladderTeams": [
                { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 2, "favoriteRace": "zerg" } ], "mmr": 4000 }
              ]
            }
            """;
        const string protossLadder = """
            {
              "ladderTeams": [
                { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 2, "favoriteRace": "protoss" } ], "mmr": 5239 }
              ]
            }
            """;

        Sc2LadderService service = CreateService(twoLadders, zergLadder, protossLadder);

        // Zerg ladder is visited first and would be the fallback, but the Protoss one is the real match.
        Assert.Equal(5239, await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_NoLadderMemberships_ReturnsNull()
    {
        const string empty = """{ "showCaseEntries": [], "placementMatches": [], "allLadderMemberships": [] }""";
        Sc2LadderService service = CreateService(empty);

        Assert.Null(await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_OnlyTeamModes_ReturnsNull()
    {
        // Team ladders carry their own separate rating that can't be compared to a 1v1 replay's.
        const string teamOnly = """
            {
              "allLadderMemberships": [
                { "ladderId": "500", "localizedGameMode": "2v2 Platinum", "rank": 3 },
                { "ladderId": "501", "localizedGameMode": "4v4 Gold", "rank": 7 }
              ]
            }
            """;
        Sc2LadderService service = CreateService(teamOnly);

        Assert.Null(await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmr_NoToken_ReturnsNullWithoutCallingApi()
    {
        StubHandler handler = new();
        Sc2LadderService service = new(new HttpClient(handler), new StubTokenProvider(null), new MockLogger());

        Assert.Null(await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetCurrentMmr_ApiReturnsError_ReturnsNull()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "");
        Sc2LadderService service = new(new HttpClient(handler), new StubTokenProvider("token"), new MockLogger());

        Assert.Null(await service.GetCurrentMmrAsync(EuProfile, LadderRace.P, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentMmrByRace_ReturnsOneEntryPerPlacedRace()
    {
        const string twoLadders = """
            {
              "allLadderMemberships": [
                { "ladderId": "1", "localizedGameMode": "1v1 Master", "rank": 1 },
                { "ladderId": "2", "localizedGameMode": "1v1 Diamond", "rank": 2 }
              ]
            }
            """;
        const string protossLadder = """
            {
              "ladderTeams": [
                { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 2, "favoriteRace": "protoss" } ], "mmr": 5239 }
              ]
            }
            """;
        const string zergLadder = """
            {
              "ladderTeams": [
                { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 2, "favoriteRace": "zerg" } ], "mmr": 4100 }
              ]
            }
            """;

        Sc2LadderService service = CreateService(twoLadders, protossLadder, zergLadder);

        IReadOnlyDictionary<LadderRace, long> byRace = await service.GetCurrentMmrAllRacesAsync(EuProfile, CancellationToken.None);

        Assert.Equal(2, byRace.Count);
        Assert.Equal(5239, byRace[LadderRace.P]);
        Assert.Equal(4100, byRace[LadderRace.Z]);
    }

    [Fact]
    public async Task GetCurrentMmrByRace_IncludesRandomLadder()
    {
        // Random is its own ladder with its own rating, not an absence of a race.
        Sc2LadderService service = CreateService(SummaryWithOne1v1Ladder, RandomLadder);

        IReadOnlyDictionary<LadderRace, long> byRace = await service.GetCurrentMmrAllRacesAsync(EuProfile, CancellationToken.None);

        Assert.Equal(3900, byRace[LadderRace.R]);
    }

    [Fact]
    public async Task GetCurrentMmr_RandomQueue_PrefersRandomLadderOverSpawnedRace()
    {
        // A Random player who also ladders Protoss must not have a Protoss game's rating attributed to
        // them just because Random happened to spawn Protoss that game.
        const string twoLadders = """
            {
              "allLadderMemberships": [
                { "ladderId": "1", "localizedGameMode": "1v1 Master", "rank": 1 },
                { "ladderId": "2", "localizedGameMode": "1v1 Diamond", "rank": 2 }
              ]
            }
            """;
        Sc2LadderService service = CreateService(twoLadders, LadderWithSelfProtoss, RandomLadder);

        Assert.Equal(3900, await service.GetCurrentMmrAsync(EuProfile, LadderRace.R, CancellationToken.None));
    }

    [Theory]
    [InlineData('P', false, LadderRace.P)]
    [InlineData('Z', false, LadderRace.Z)]
    [InlineData('T', false, LadderRace.T)]
    // Queueing Random earns Random MMR no matter which race actually spawned.
    [InlineData('P', true, LadderRace.R)]
    [InlineData('Z', true, LadderRace.R)]
    public void FromPlayer_MapsSpawnedRaceAndRandomFlagToLadder(char spawned, bool random, LadderRace expected)
    {
        Assert.Equal(expected, LadderRaceExtensions.FromPlayer(spawned, random));
    }

    [Fact]
    public async Task GetCurrentMmrByRace_IgnoresOtherPlayersAndTeamModes()
    {
        const string mixedModes = """
            {
              "allLadderMemberships": [
                { "ladderId": "1", "localizedGameMode": "1v1 Master", "rank": 1 },
                { "ladderId": "2", "localizedGameMode": "2v2 Gold", "rank": 4 }
              ]
            }
            """;
        Sc2LadderService service = CreateService(mixedModes, LadderWithSelfProtoss);

        IReadOnlyDictionary<LadderRace, long> byRace = await service.GetCurrentMmrAllRacesAsync(EuProfile, CancellationToken.None);

        // Only the 1v1 ladder is fetched at all, and only this profile's own team within it counts.
        RaceMmr single = Assert.Single(byRace.Select(kv => new RaceMmr(kv.Key, kv.Value)));
        Assert.Equal(new RaceMmr(LadderRace.P, 5239), single);
    }

    [Fact]
    public async Task GetCurrentMmrByRace_NoCredentials_ReturnsEmpty()
    {
        Sc2LadderService service = new(new HttpClient(new StubHandler()), new StubTokenProvider(null), new MockLogger());

        Assert.Empty(await service.GetCurrentMmrAllRacesAsync(EuProfile, CancellationToken.None));
    }

    private record RaceMmr(LadderRace Race, long Mmr);

    private static Sc2LadderService CreateService(params string[] responses)
    {
        StubHandler handler = new();
        foreach (string body in responses)
            handler.Enqueue(HttpStatusCode.OK, body);
        return new Sc2LadderService(new HttpClient(handler), new StubTokenProvider("token"), new MockLogger());
    }

    private sealed class StubTokenProvider(string? token)
        : BlizzardAppTokenProvider(null!, null!, null!, new MockLogger())
    {
        public override Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public int RequestCount { get; private set; }

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") });

            (HttpStatusCode status, string body) = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
