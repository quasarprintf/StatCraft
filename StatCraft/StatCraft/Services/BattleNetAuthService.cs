using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services
{
    public record BattleNetTokenResult(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset ExpiresAtUtc,
        string BattleTag,
        string AccountSub);

    public enum AuthFailureReason
    {
        UserCancelled,
        Timeout,
        PortInUse,
        StateMismatch,
        TokenExchangeFailed,
        UserInfoFailed,
        BrowserLaunchFailed,
    }

    public class BattleNetAuthException : Exception
    {
        public AuthFailureReason Reason { get; }

        public BattleNetAuthException(AuthFailureReason reason, string message, Exception? inner = null)
            : base(message, inner)
        {
            Reason = reason;
        }
    }

    public class BattleNetAuthService
    {
        public const string RedirectUri = "http://localhost:51820/callback";
        private const string ListenerPrefix = "http://localhost:51820/callback/";
        private const string AuthorizeEndpoint = "https://oauth.battle.net/authorize";
        private const string TokenEndpoint = "https://oauth.battle.net/token";
        private const string UserInfoEndpoint = "https://oauth.battle.net/userinfo";
        private const string Scope = "openid sc2.profile";

        private readonly HttpClient _httpClient;

        public BattleNetAuthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<BattleNetTokenResult> LinkAccountAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
        {
            string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            using HttpListener listener = new HttpListener();
            listener.Prefixes.Add(ListenerPrefix);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                throw new BattleNetAuthException(AuthFailureReason.PortInUse, "The local callback port is already in use by another application.", ex);
            }

            try
            {
                string authorizeUrl = $"{AuthorizeEndpoint}?response_type=code" +
                    $"&client_id={Uri.EscapeDataString(clientId)}" +
                    $"&scope={Uri.EscapeDataString(Scope)}" +
                    $"&state={Uri.EscapeDataString(state)}" +
                    $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";

                try
                {
                    Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    throw new BattleNetAuthException(AuthFailureReason.BrowserLaunchFailed, "Could not open the system browser.", ex);
                }

                HttpListenerContext context = await WaitForCallbackAsync(listener, cancellationToken);

                System.Collections.Specialized.NameValueCollection query = context.Request.QueryString;
                string? receivedState = query["state"];
                string? code = query["code"];

                bool isValid = receivedState == state && !string.IsNullOrEmpty(code);
                await RespondToBrowserAsync(context, isValid);

                if (!isValid)
                    throw new BattleNetAuthException(AuthFailureReason.StateMismatch, "The login response could not be verified.");

                (string accessToken, string? refreshToken, DateTimeOffset expiresAtUtc) = await ExchangeCodeForTokensAsync(code!, clientId, clientSecret);
                (string battleTag, string sub) = await FetchUserInfoAsync(accessToken);

                return new BattleNetTokenResult(accessToken, refreshToken, expiresAtUtc, battleTag, sub);
            }
            finally
            {
                listener.Stop();
                listener.Close();
            }
        }

        private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            Task<HttpListenerContext> contextTask = listener.GetContextAsync();
            Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);

            Task completed = await Task.WhenAny(contextTask, timeoutTask);
            if (completed == contextTask)
                return await contextTask;

            if (cancellationToken.IsCancellationRequested)
                throw new BattleNetAuthException(AuthFailureReason.UserCancelled, "Linking was cancelled.");

            throw new BattleNetAuthException(AuthFailureReason.Timeout, "Login timed out. Try again.");
        }

        private static async Task RespondToBrowserAsync(HttpListenerContext context, bool success)
        {
            string message = success
                ? "<html><body>You can close this tab and return to StatCraft.</body></html>"
                : "<html><body>Something went wrong. You can close this tab and return to StatCraft.</body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.OutputStream.Close();
        }

        private async Task<(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc)> ExchangeCodeForTokensAsync(string code, string clientId, string clientSecret)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                new System.Collections.Generic.KeyValuePair<string, string>("code", code),
                new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", RedirectUri),
            });

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                throw new BattleNetAuthException(AuthFailureReason.TokenExchangeFailed, "Could not reach Battle.net to exchange the login code.", ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new BattleNetAuthException(AuthFailureReason.TokenExchangeFailed, $"Battle.net rejected the login code (HTTP {(int)response.StatusCode}).");

            TokenResponse? token;
            try
            {
                token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            }
            catch (Exception ex)
            {
                throw new BattleNetAuthException(AuthFailureReason.TokenExchangeFailed, "Battle.net returned an unexpected response.", ex);
            }

            if (token is null || string.IsNullOrEmpty(token.AccessToken))
                throw new BattleNetAuthException(AuthFailureReason.TokenExchangeFailed, "Battle.net returned an unexpected response.");

            return (token.AccessToken, token.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));
        }

        private async Task<(string BattleTag, string Sub)> FetchUserInfoAsync(string accessToken)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                throw new BattleNetAuthException(AuthFailureReason.UserInfoFailed, "Could not reach Battle.net to fetch account info.", ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new BattleNetAuthException(AuthFailureReason.UserInfoFailed, $"Battle.net rejected the account info request (HTTP {(int)response.StatusCode}).");

            UserInfoResponse? userInfo;
            try
            {
                userInfo = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
            }
            catch (Exception ex)
            {
                throw new BattleNetAuthException(AuthFailureReason.UserInfoFailed, "Battle.net returned an unexpected response.", ex);
            }

            if (userInfo is null || string.IsNullOrEmpty(userInfo.BattleTag))
                throw new BattleNetAuthException(AuthFailureReason.UserInfoFailed, "Battle.net returned an unexpected response.");

            return (userInfo.BattleTag, userInfo.Sub);
        }

        private record TokenResponse(
            [property: JsonPropertyName("access_token")] string AccessToken,
            [property: JsonPropertyName("refresh_token")] string? RefreshToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);

        private record UserInfoResponse(
            [property: JsonPropertyName("sub")] string Sub,
            [property: JsonPropertyName("battletag")] string BattleTag);
    }
}
