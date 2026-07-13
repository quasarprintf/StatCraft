using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StatCraft.Models;

namespace StatCraft.Services
{
    public class StarCraft2ProfileService
    {
        // Any regional host returns the account's profiles across every region, so we only
        // need one successful response. Multiple hosts are tried in case one is unavailable.
        private static readonly string[] Hosts =
        {
            "https://us.api.blizzard.com",
            "https://eu.api.blizzard.com",
            "https://kr.api.blizzard.com",
            "https://tw.api.blizzard.com",
        };

        private readonly HttpClient _httpClient;

        public StarCraft2ProfileService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<Sc2Profile>> GetProfilesAsync(string accountId, string accessToken, CancellationToken cancellationToken)
        {
            foreach (var host in Hosts)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/sc2/player/{accountId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, cancellationToken);
                }
                catch
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    continue;

                List<PlayerResponse>? entries;
                try
                {
                    entries = await response.Content.ReadFromJsonAsync<List<PlayerResponse>>(cancellationToken: cancellationToken);
                }
                catch
                {
                    continue;
                }

                if (entries is null)
                    continue;

                return entries.ConvertAll(entry => new Sc2Profile
                {
                    RegionLabel = Sc2Regions.GetLabel(entry.RegionId),
                    RegionId = entry.RegionId,
                    RealmId = entry.RealmId,
                    ProfileId = entry.ProfileId,
                    Name = entry.Name,
                });
            }

            return [];
        }

        private record PlayerResponse(
            [property: JsonPropertyName("name")] string Name,
            [property: JsonConverter(typeof(FlexibleStringConverter)), JsonPropertyName("profileId")] string ProfileId,
            [property: JsonConverter(typeof(FlexibleStringConverter)), JsonPropertyName("regionId")] string RegionId,
            [property: JsonConverter(typeof(FlexibleStringConverter)), JsonPropertyName("realmId")] string RealmId);

        // Blizzard's SC2 API returns regionId/realmId/profileId as JSON numbers rather than strings.
        private class FlexibleStringConverter : JsonConverter<string>
        {
            public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType == JsonTokenType.Number
                    ? reader.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : reader.GetString() ?? "";

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value);
        }
    }
}
