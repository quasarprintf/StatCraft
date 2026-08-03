using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StatCraft.Models.Battlenet;
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

        // Returns null whenever MMR can't be determined — no credentials, network failure, or (commonly)
        // the profile simply has no placed ladder for the current season. None of those are errors worth
        // interrupting a replay import over.
        public async Task<long?> GetCurrentMmrAsync(Sc2Profile profile, char race, CancellationToken cancellationToken)
        {
            string? token = await tokenProvider.GetTokenAsync(cancellationToken);
            if (token == null)
                return null;

            string basePath = $"{HostFor(profile.RegionId)}/sc2/profile/{profile.RegionId}/{profile.RealmId}/{profile.ProfileId}";

            LadderSummaryResponse? summary = await GetJsonAsync<LadderSummaryResponse>($"{basePath}/ladder/summary", token, cancellationToken);
            if (summary?.AllLadderMemberships == null || summary.AllLadderMemberships.Count == 0)
                return null;

            // 1v1 only: team modes have their own MMR that doesn't correspond to a solo replay's rating.
            List<LadderMembership> candidates = summary.AllLadderMemberships
                .Where(m => m.LocalizedGameMode?.StartsWith("1v1", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (candidates.Count == 0)
                return null;

            long? fallbackMmr = null;
            foreach (LadderMembership membership in candidates)
            {
                LadderResponse? ladder = await GetJsonAsync<LadderResponse>($"{basePath}/ladder/{membership.LadderId}", token, cancellationToken);
                if (ladder?.LadderTeams == null)
                    continue;

                foreach (LadderTeam team in ladder.LadderTeams)
                {
                    LadderTeamMember? self = team.TeamMembers?.FirstOrDefault(m => IsProfile(m, profile));
                    if (self == null)
                        continue;

                    // Exact race match wins. Otherwise keep it as a fallback so a Random game (whose
                    // replay race is the spawned race, not the queued one) still resolves to something.
                    if (MatchesRace(self.FavoriteRace, race))
                        return team.Mmr;

                    fallbackMmr ??= team.Mmr;
                }
            }

            return fallbackMmr;
        }

        private static bool IsProfile(LadderTeamMember member, Sc2Profile profile) =>
            member.Id == profile.ProfileId.ToString()
            && member.Realm.ToString() == profile.RealmId
            && member.Region.ToString() == profile.RegionId;

        private static bool MatchesRace(string? favoriteRace, char race) => (favoriteRace?.ToLowerInvariant(), race) switch
        {
            ("zerg", 'Z') => true,
            ("terran", 'T') => true,
            ("protoss", 'P') => true,
            _ => false,
        };

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
