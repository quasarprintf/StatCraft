using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Services.BattlenetApi
{
    // Supplies a client-credentials ("app") token for the SC2 community endpoints.
    //
    // Deliberately separate from BattleNetAuthService, which does the interactive authorization-code
    // flow for /sc2/player/{accountId}. The ladder endpoints are not user-scoped, so they only need an
    // app token — which matters because Battle.net issues no refresh token for the user flow here, so a
    // user token dies ~24h after linking with no way back except re-linking. An app token can be minted
    // on demand forever from credentials the user already saved when they linked.
    public class BlizzardAppTokenProvider(AccountRepository accountRepository, TokenProtector tokenProtector,
        HttpClient httpClient, ILogger logger)
    {
        public const string ClientIdSettingKey = "BlizzardClientId";
        public const string ClientSecretSettingKey = "BlizzardClientSecretEncryptedB64";

        private const string TokenEndpoint = "https://oauth.battle.net/token";

        // Renew slightly early so a token can't expire mid-request.
        private static readonly TimeSpan ExpiryGuard = TimeSpan.FromMinutes(2);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private string? _cachedToken;
        private DateTimeOffset _cachedTokenExpiresAtUtc;

        // Returns null (rather than throwing) when credentials are missing or Battle.net is unreachable —
        // callers treat MMR lookup as best-effort and must degrade gracefully.
        // Virtual so tests can supply a token without real saved credentials.
        public virtual async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (IsCachedTokenUsable())
                return _cachedToken;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                // Another caller may have refreshed while we waited on the gate.
                if (IsCachedTokenUsable())
                    return _cachedToken;

                return await RequestTokenAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private bool IsCachedTokenUsable() =>
            _cachedToken != null && DateTimeOffset.UtcNow + ExpiryGuard < _cachedTokenExpiresAtUtc;

        private async Task<string?> RequestTokenAsync(CancellationToken cancellationToken)
        {
            string? clientId = accountRepository.GetSetting(ClientIdSettingKey);
            string? encryptedSecretB64 = accountRepository.GetSetting(ClientSecretSettingKey);
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(encryptedSecretB64))
            {
                logger.LogWarning("No Blizzard API credentials saved; skipping MMR lookup.");
                return null;
            }

            string clientSecret;
            try
            {
                clientSecret = tokenProtector.Decrypt(Convert.FromBase64String(encryptedSecretB64));
            }
            catch (Exception ex)
            {
                logger.LogError($"Could not decrypt the saved Blizzard client secret: {ex.Message}");
                return null;
            }

            using HttpRequestMessage request = new(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
            request.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);

            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning($"Battle.net rejected the app token request (HTTP {(int)response.StatusCode}).");
                    return null;
                }

                TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
                if (token == null || string.IsNullOrEmpty(token.AccessToken))
                {
                    logger.LogWarning("Battle.net returned an unexpected app token response.");
                    return null;
                }

                _cachedToken = token.AccessToken;
                _cachedTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
                return _cachedToken;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not reach Battle.net for an app token: {ex.Message}");
                return null;
            }
        }

        private record TokenResponse(
            [property: JsonPropertyName("access_token")] string AccessToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);
    }
}
