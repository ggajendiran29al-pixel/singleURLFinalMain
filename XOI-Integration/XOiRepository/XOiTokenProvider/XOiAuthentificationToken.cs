using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiDataModels;
using Newtonsoft.Json;

namespace XOI_Integration.XOiRepository.XOiTokenProvider
{
    public class XOiAuthentificationToken
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private static XOiToken _cachedToken;
        private static DateTime _validUntilUtc = DateTime.MinValue;

        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

        public async Task<XOiToken> GetAuthTokenAsync()
        {
            if (_cachedToken != null && DateTime.UtcNow < (_validUntilUtc - RefreshSkew))
                return _cachedToken;

            await _lock.WaitAsync();
            try
            {
                if (_cachedToken != null && DateTime.UtcNow < (_validUntilUtc - RefreshSkew))
                    return _cachedToken;

                string tokenURL = Environment.GetEnvironmentVariable("XOiAPIGetTokenURL", EnvironmentVariableTarget.Process);
                string apiKey = Environment.GetEnvironmentVariable("XOIAPIKey", EnvironmentVariableTarget.Process);
                string apiSecret = Environment.GetEnvironmentVariable("XOIAPISecret", EnvironmentVariableTarget.Process);

                var requestBody = new { api_key = apiKey, api_secret = apiSecret };
                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                var start = DateTime.UtcNow;
                var response = await _httpClient.PostAsync(tokenURL, content);
                var duration = DateTime.UtcNow - start;

                Console.WriteLine($"[XOiAuthToken] Request completed. Status={response.StatusCode}, DurationMs={duration.TotalMilliseconds}");

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Failed to retrieve authentication token. Status code: {response.StatusCode}");

                var token = JsonConvert.DeserializeObject<XOiToken>(await response.Content.ReadAsStringAsync());
                if (token == null || string.IsNullOrWhiteSpace(token.Token))
                    throw new Exception("Token endpoint returned empty token.");

                _cachedToken = token;
                _validUntilUtc = DateTime.UtcNow.Add(TokenLifetime);

                Console.WriteLine($"[XOiAuthToken] Token valid until {_validUntilUtc} UTC");

                return _cachedToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void InvalidateToken()
        {
            _cachedToken = null;
            _validUntilUtc = DateTime.MinValue;
            Console.WriteLine($"[XOiAuthToken] Token invalidated at {DateTime.UtcNow}");
        }
    }
}