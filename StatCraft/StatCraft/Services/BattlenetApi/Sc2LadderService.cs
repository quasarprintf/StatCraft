using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BackgroundService;

namespace StatCraft.Services.BattlenetApi
{
    // Reads a profile's current ladder MMR. Response shapes here were confirmed against the live API:
    //
    //   /sc2/profile/{region}/{realm}/{profileId}/ladder/summary
    //     → allLadderMemberships[] { ladderId, localizedGameMode: "1v1 Master", rank }
    //   /sc2/profile/{region}/{realm}/{profileId}/ladder/{ladderId}
    //     → ladderTeams[] { teamMembers[] { id, realm, region, favoriteRace }, mmr, wins, losses, ... }
    //
    // Note allLadderMemberships carries no race, so the ladder itself has to be fetched to tell one
    // race's 1v1 ladder from another's.
    public class Sc2LadderService(HttpClient httpClient, BlizzardAppTokenProvider tokenProvider, ILogger logger)
    {
        // The community endpoints are region-scoped; a profile must be queried on its own region's host.
        private static string HostFor(string regionId) => regionId switch
        {
            "1" => "https://us.api.blizzard.com",
            "2" => "https://eu.api.blizzard.com",
            "3" => "https://kr.api.blizzard.com",
            "5" => "https://kr.api.blizzard.com",
            _ => "https://us.api.blizzard.com",
        };

        // The freshest ranked MMR seen for each ladder, from either an API lookup or a resolved post-game poll
        private readonly Dictionary<int, Dictionary<LadderRace, long>> _lastKnownMmr = new();
        private readonly object _lastKnownMmrGate = new();

        public long? GetLastKnownMmr(Sc2Profile profile, LadderRace race)
        {
            lock (_lastKnownMmrGate)
                if (_lastKnownMmr.TryGetValue(profile.Id, out Dictionary<LadderRace, long>? raceDict))
                    return raceDict.TryGetValue(race, out long mmr) ? mmr : null;
                else
                    return null;
        }

        // Called when a post-game poll observes a new ranked rating, so the next game is compared against
        // the value it should actually have started from.
        private void RecordObservedMmr(Sc2Profile profile, LadderRace race, long mmr)
        {
            lock (_lastKnownMmrGate)
            {
                if (!_lastKnownMmr.ContainsKey(profile.Id))
                    _lastKnownMmr[profile.Id] = new Dictionary<LadderRace, long>();
                _lastKnownMmr[profile.Id][race] = mmr;
            }
        }

        // Every 1v1 rating this profile currently holds, keyed by the ladder it was earned on. SC2 rates
        // each race separately — and Random separately again — so a player who ladders as more than one
        // has more than one MMR and there is no single "current rating" to show. Empty whenever nothing
        // could be determined.
        public async Task<IReadOnlyDictionary<LadderRace, long>> GetCurrentMmrAllRacesAsync(Sc2Profile profile, CancellationToken cancellationToken)
        {
            await CacheAllRankedOneVsOne(profile, cancellationToken);
            if (_lastKnownMmr.TryGetValue(profile.Id, out Dictionary<LadderRace, long>? raceDict))
                return raceDict;
            else
                return new Dictionary<LadderRace, long>();
        }

        // Returns null whenever MMR can't be determined — no credentials, network failure, or (commonly)
        // the profile simply has no placed ladder for the current season. None of those are errors worth
        // interrupting a replay import over.
        public async Task<long?> GetCurrentMmrAsync(Sc2Profile profile, LadderRace ladderRace, CancellationToken cancellationToken)
        {
            await CacheAllRankedOneVsOne(profile, cancellationToken);
            return GetLastKnownMmr(profile, ladderRace);
        }

        // Shared fetch behind both public methods: resolves every 1v1 ladder this profile sits in and
        // pulls out the team row that is actually theirs.
        private async Task CacheAllRankedOneVsOne(Sc2Profile profile, CancellationToken cancellationToken)
        {
            List<(LadderRace? Race, long Mmr)> results = [];

            string? token = await tokenProvider.GetTokenAsync(cancellationToken);
            if (token == null)
                return;

            string basePath = $"{HostFor(profile.RegionId)}/sc2/profile/{profile.RegionId}/{profile.RealmId}/{profile.ProfileId}";

            LadderSummaryResponse? summary = await GetJsonAsync<LadderSummaryResponse>($"{basePath}/ladder/summary", token, cancellationToken);
            if (summary?.AllLadderMemberships == null)
                return;

            // 1v1 only: it's not obvious which team to load for 2v2/3v3/4v4 since it's separate for each teammate
            List<LadderMembership> candidates = summary.AllLadderMemberships
                .Where(m => m.LocalizedGameMode?.StartsWith("1v1", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (LadderMembership membership in candidates)
            {
                LadderResponse? ladder = await GetJsonAsync<LadderResponse>($"{basePath}/ladder/{membership.LadderId}", token, cancellationToken);
                if (ladder?.LadderTeams == null)
                    continue;

                foreach (LadderTeam team in ladder.LadderTeams)
                {
                    LadderTeamMember? self = team.TeamMembers?.FirstOrDefault(m => IsProfile(m, profile));
                    if (self != null && team.Mmr.HasValue)
                        RecordObservedMmr(profile, ParseRace(self.FavoriteRace)!.Value, team.Mmr.Value);
                }
            }
        }

        private static bool IsProfile(LadderTeamMember member, Sc2Profile profile) =>
            member.Id == profile.ProfileId.ToString()
            && member.Realm.ToString() == profile.RealmId
            && member.Region.ToString() == profile.RegionId;

        private LadderRace? ParseRace(string? favoriteRace)
        {
            switch (favoriteRace?.ToLowerInvariant())
            {
                case "zerg": return LadderRace.Z;
                case "terran": return LadderRace.T;
                case "protoss": return LadderRace.P;
                case "random": return LadderRace.R;
                case null or "": return null;
                default:
                    // Logged rather than silently dropped: an unrecognised value here means Blizzard uses
                    // a token we don't know about, which would quietly hide that ladder's rating.
                    logger.LogWarning($"Unrecognised ladder favoriteRace \"{favoriteRace}\"; its rating will be ignored.");
                    return null;
            }
        }

        private async Task<T?> GetJsonAsync<T>(string url, string token, CancellationToken cancellationToken) where T : class
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning($"Ladder request failed (HTTP {(int)response.StatusCode}): {url}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Ladder request errored ({ex.GetType().Name}): {url}");
                return null;
            }
        }

        private class LadderSummaryResponse
        {
            [JsonPropertyName("allLadderMemberships")]
            public List<LadderMembership>? AllLadderMemberships { get; set; }
        }

        private class LadderMembership
        {
            [JsonPropertyName("ladderId")] public string LadderId { get; set; } = "";
            [JsonPropertyName("localizedGameMode")] public string? LocalizedGameMode { get; set; }
        }

        private class LadderResponse
        {
            [JsonPropertyName("ladderTeams")] public List<LadderTeam>? LadderTeams { get; set; }
        }

        private class LadderTeam
        {
            [JsonPropertyName("teamMembers")] public List<LadderTeamMember>? TeamMembers { get; set; }
            [JsonPropertyName("mmr")] public long? Mmr { get; set; }
        }

        private class LadderTeamMember
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("realm")] public int Realm { get; set; }
            [JsonPropertyName("region")] public int Region { get; set; }
            [JsonPropertyName("favoriteRace")] public string? FavoriteRace { get; set; }
        }
    }
}
