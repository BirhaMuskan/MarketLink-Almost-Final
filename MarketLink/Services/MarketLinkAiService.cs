using System.Net.Http.Json;

namespace MarketLink.Services
{
    public class MarketLinkAiService : IMarketLinkAiService
    {
        private readonly HttpClient _httpClient;

        public MarketLinkAiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<AiForecastResponse> ForecastAsync(
            AiForecastRequest request,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "forecast-demand",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                throw new InvalidOperationException(
                    $"AI service returned {(int)response.StatusCode}: {body}");
            }

            var result =
                await response.Content.ReadFromJsonAsync<AiForecastResponse>(
                    cancellationToken: cancellationToken);

            return result
                ?? throw new InvalidOperationException(
                    "AI service returned an empty response.");
        }
    }
}
