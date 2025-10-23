using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository.XOiTokenProvider
{
    public class XOiAuthentificationToken
    {
        private readonly HttpClient _httpClient;

        public XOiAuthentificationToken()
        {
            _httpClient = new HttpClient();
        }

        public async Task<XOiToken> GetAuthTokenAsync()
        {
            string tokenURL = System.Environment.GetEnvironmentVariable("XOiAPIGetTokenURL", EnvironmentVariableTarget.Process);
            string apiKey = System.Environment.GetEnvironmentVariable("XOIAPIKey", EnvironmentVariableTarget.Process);
            string apiSecret = System.Environment.GetEnvironmentVariable("XOiAPISecret", EnvironmentVariableTarget.Process);

            var requestBody = new
            {
                api_key = apiKey,
                api_secret = apiSecret
            };

            var requestBodyJson = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(tokenURL, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();

                return Newtonsoft.Json.JsonConvert.DeserializeObject<XOiToken>(responseContent);
            }
            else
            {
                throw new Exception($"Failed to retrieve authentication token. Status code: {response.StatusCode}");
            }
        }
    }
}
