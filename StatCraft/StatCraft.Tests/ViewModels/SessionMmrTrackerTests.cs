using System.Net;
using System.Net.Http;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BattlenetApi;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class SessionMmrTrackerTests
{
    private static Sc2Profile Profile => new() { RegionId = "1", RealmId = "1", ProfileId = 1234567, Name = "TestPlayer" };

    [Fact]
    public void SetBaseline_PopulatesCurrentMmrsSortedByRace()
    {
        SessionMmrTracker tracker = CreateTracker();

        // Given out of enum order (Z, T, P, R) on purpose, to prove SetBaseline sorts rather than just
        // reflecting dictionary enumeration order.
        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Random] = 3900, [LadderRace.Protoss] = 5239, [LadderRace.Zerg] = 4100 });

        Assert.Equal([LadderRace.Zerg, LadderRace.Protoss, LadderRace.Random], tracker.CurrentMmrs.Select(m => m.Race));
    }

    [Fact]
    public void SetBaseline_FreshLookup_HasNoMovementYet()
    {
        SessionMmrTracker tracker = CreateTracker();

        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Protoss] = 5239 });

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Equal(5239, entry.Mmr);
        Assert.Equal(5239, entry.SessionStartMmr);
        Assert.Equal(0, entry.SessionChange);
    }

    // A second lookup (e.g. from a slow LoadCurrentMmrs call that finally lands) fully replaces the
    // baseline rather than merging into it — otherwise a race dropped from the second response would
    // linger from the first.
    [Fact]
    public void SetBaseline_CalledTwice_ReplacesRatherThanMerges()
    {
        SessionMmrTracker tracker = CreateTracker();

        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Protoss] = 5000, [LadderRace.Zerg] = 4000 });
        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Terran] = 4500 });

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Equal(LadderRace.Terran, entry.Race);
    }

    [Fact]
    public void SeedBaselineIfAbsent_NoExistingBaseline_BecomesTheBaseline()
    {
        SessionMmrTracker tracker = CreateTracker();

        tracker.SeedBaselineIfAbsent(LadderRace.Zerg, 4100);
        tracker.UpdateCurrent(LadderRace.Zerg, 4124);

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Equal(4100, entry.SessionStartMmr);
        Assert.Equal(24, entry.SessionChange);
    }

    // The core invariant SeedBaselineIfAbsent exists for: the session-start API lookup is authoritative,
    // and a replay-derived seed for a ladder that lookup already covered must never override it — that
    // would make the very first game after session start look like a jump from a stale value.
    [Fact]
    public void SeedBaselineIfAbsent_BaselineAlreadySet_DoesNotOverwriteIt()
    {
        SessionMmrTracker tracker = CreateTracker();
        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Protoss] = 5239 });

        tracker.SeedBaselineIfAbsent(LadderRace.Protoss, 4900);
        tracker.UpdateCurrent(LadderRace.Protoss, 5263);

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Equal(5239, entry.SessionStartMmr);
    }

    [Fact]
    public void UpdateCurrent_NoBaseline_SessionStartMmrIsNull()
    {
        SessionMmrTracker tracker = CreateTracker();

        tracker.UpdateCurrent(LadderRace.Protoss, 5239);

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Null(entry.SessionStartMmr);
        Assert.Null(entry.SessionChange);
    }

    [Fact]
    public void UpdateCurrent_SameRaceAgain_ReplacesRatherThanDuplicates()
    {
        SessionMmrTracker tracker = CreateTracker();

        tracker.UpdateCurrent(LadderRace.Protoss, 5239);
        tracker.UpdateCurrent(LadderRace.Protoss, 5263);

        RaceMmrViewModel entry = Assert.Single(tracker.CurrentMmrs);
        Assert.Equal(5263, entry.Mmr);
    }

    [Fact]
    public void UpdateCurrent_NewRaces_InsertedInSortedPosition()
    {
        SessionMmrTracker tracker = CreateTracker();

        // Applied out of enum order (Z, T, P, R) on purpose.
        tracker.UpdateCurrent(LadderRace.Zerg, 4100);
        tracker.UpdateCurrent(LadderRace.Terran, 4500);
        tracker.UpdateCurrent(LadderRace.Protoss, 5239);
        tracker.UpdateCurrent(LadderRace.Random, 3900);

        Assert.Equal([LadderRace.Zerg, LadderRace.Terran, LadderRace.Protoss, LadderRace.Random], tracker.CurrentMmrs.Select(m => m.Race));
    }

    // Updating one race's rating must not disturb another's already-established baseline.
    [Fact]
    public void UpdateCurrent_DoesNotAffectOtherRacesBaselines()
    {
        SessionMmrTracker tracker = CreateTracker();
        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Protoss] = 5239, [LadderRace.Zerg] = 4100 });

        tracker.UpdateCurrent(LadderRace.Protoss, 5263);

        RaceMmrViewModel zergEntry = tracker.CurrentMmrs.Single(m => m.Race == LadderRace.Zerg);
        Assert.Equal(4100, zergEntry.SessionStartMmr);
    }

    [Fact]
    public void Reset_ClearsCurrentMmrsAndBaseline()
    {
        SessionMmrTracker tracker = CreateTracker();
        tracker.SetBaseline(new Dictionary<LadderRace, long> { [LadderRace.Protoss] = 5239 });

        tracker.Reset();

        Assert.Empty(tracker.CurrentMmrs);

        // Proves the baseline itself was cleared too, not just the display list — otherwise this would
        // pick up the stale 5239 baseline from before Reset.
        tracker.UpdateCurrent(LadderRace.Protoss, 4000);
        Assert.Null(Assert.Single(tracker.CurrentMmrs).SessionStartMmr);
    }

    [Fact]
    public async Task FetchCurrentMmrs_LadderServiceSucceeds_ReturnsItsResult()
    {
        const string summary = """
            {
              "allLadderMemberships": [
                { "ladderId": "1", "localizedGameMode": "1v1 Master", "rank": 1 }
              ]
            }
            """;
        const string ladder = """
            {
              "ladderTeams": [
                { "teamMembers": [ { "id": "1234567", "realm": 1, "region": 1, "favoriteRace": "protoss" } ], "mmr": 5239 }
              ]
            }
            """;
        SessionMmrTracker tracker = new(CreateLadderService(summary, ladder));

        IReadOnlyDictionary<LadderRace, long>? byRace = await tracker.FetchCurrentMmrs(Profile, CancellationToken.None);

        Assert.NotNull(byRace);
        Assert.Equal(5239, byRace[LadderRace.Protoss]);
    }

    [Fact]
    public async Task FetchCurrentMmrs_NoCredentials_ReturnsEmptyRatherThanNull()
    {
        // Mirrors Sc2LadderService.GetCurrentMmrByRaceAsync's own contract: missing credentials is a
        // normal "nothing to show" outcome, not a failure — null is reserved for FetchCurrentMmrs' own
        // best-effort catch.
        SessionMmrTracker tracker = new(new Sc2LadderService(new HttpClient(new StubHandler()), new StubTokenProvider(null), new MockLogger()));

        IReadOnlyDictionary<LadderRace, long>? byRace = await tracker.FetchCurrentMmrs(Profile, CancellationToken.None);

        Assert.NotNull(byRace);
        Assert.Empty(byRace);
    }

    private static SessionMmrTracker CreateTracker() =>
        new(new Sc2LadderService(new HttpClient(new StubHandler()), new StubTokenProvider(null), new MockLogger()));

    private static Sc2LadderService CreateLadderService(params string[] responses)
    {
        StubHandler handler = new();
        foreach (string body in responses)
            handler.Enqueue(HttpStatusCode.OK, body);
        return new Sc2LadderService(new HttpClient(handler), new StubTokenProvider("token"), new MockLogger());
    }

    private sealed class StubTokenProvider(string? token) : BlizzardAppTokenProvider(null!, null!, null!, new MockLogger())
    {
        public override Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
