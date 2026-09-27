using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketLink.Services
{
    public interface IGroqAssistantService
    {
        bool IsConfigured { get; }

        Task<GroqIntentResult?> InterpretAsync(
            string question,
            IReadOnlyList<GroqConversationTurn> recentConversation,
            IReadOnlyList<string> farmerNames,
            IReadOnlyList<string> marketNames,
            IReadOnlyList<string> productNames,
            CancellationToken cancellationToken = default);

        Task<string?> PolishAnswerAsync(
            string question,
            string verifiedFacts,
            IReadOnlyList<GroqConversationTurn> recentConversation,
            CancellationToken cancellationToken = default);
    }

    public sealed class GroqAssistantService : IGroqAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GroqAssistantService> _logger;

        private string ApiKey =>
            _configuration["Groq:ApiKey"]?.Trim() ?? "";

        private string Model =>
            _configuration["Groq:Model"]?.Trim()
            ?? "openai/gpt-oss-120b";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey);

        public GroqAssistantService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GroqAssistantService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<GroqIntentResult?> InterpretAsync(
            string question,
            IReadOnlyList<GroqConversationTurn> recentConversation,
            IReadOnlyList<string> farmerNames,
            IReadOnlyList<string> marketNames,
            IReadOnlyList<string> productNames,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return null;

            var systemPrompt = """
You are the intent router for MarketLink, a farmers-market marketplace.

Your job is ONLY to understand the user's MarketLink question.
Do not answer the user.

Return one JSON object with exactly these fields:
{
  "intent": "FarmerLookup|FarmerProducts|ProductSearch|ProductPrice|MarketHours|MarketLookup|PickupSlots|FarmerAvailability|Favorites|EnableRestockAlert|DisableRestockAlert|FavoriteFarmer|FavoriteProduct|FavoriteMarket|ShowNotifications|Unknown",
  "farmer": string|null,
  "market": string|null,
  "product": string|null,
  "day": string|null,
  "time": string|null,
  "useFavoriteFarmers": true|false,
  "useFavoriteProducts": true|false,
  "useFavoriteMarkets": true|false,
  "normalizedQuery": string
}

Rules:
- Resolve pronouns and follow-ups using recent conversation.
- "he", "him", "that farmer", "they" can refer to a previously discussed farmer.
- "there", "that market" can refer to a previously discussed market.
- Never invent a farmer, product, or market name.
- Prefer names from the provided MarketLink entity lists.
- "Do you have a farmer named X?" => FarmerLookup.
- "What does he sell?" or "What products does X have?" => FarmerProducts.
- Questions about price/cost => ProductPrice.
- Questions about opening/closing/timing => MarketHours.
- Questions about pickup time/slot => PickupSlots.
- Questions about farmer day/attendance/availability => FarmerAvailability.
- Product discovery such as "where can I get tomatoes" => ProductSearch.

- "notify me", "alert me", "tell me when it is back", "let me know when available" about a product => EnableRestockAlert.
- "stop notifying me", "cancel the alert", "remove restock alert" => DisableRestockAlert.
- "save/favorite this farmer" => FavoriteFarmer.
- "save/favorite this product" => FavoriteProduct.
- "save/favorite this market" => FavoriteMarket.
- "show my notifications/alerts" => ShowNotifications.
- Action intents must resolve the farmer/product/market from recent conversation when the user says "it", "this product", "him", "that farmer", or "there".
- If uncertain, use Unknown.
""";

            var context = BuildConversationText(recentConversation);

            var userPrompt = $"""
USER QUESTION:
{question}

RECENT CONVERSATION:
{context}

KNOWN FARMERS:
{JoinLimited(farmerNames, 120)}

KNOWN MARKETS:
{JoinLimited(marketNames, 120)}

KNOWN PRODUCTS:
{JoinLimited(productNames, 160)}
""";

            try
            {
                var json = await SendChatAsync(
                    systemPrompt,
                    userPrompt,
                    jsonMode: true,
                    temperature: 0.05m,
                    maxTokens: 700,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<GroqIntentResult>(
                    json,
                    options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Groq intent interpretation failed; MarketLink will use the local fallback.");
                return null;
            }
        }

        public async Task<string?> PolishAnswerAsync(
            string question,
            string verifiedFacts,
            IReadOnlyList<GroqConversationTurn> recentConversation,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured ||
                string.IsNullOrWhiteSpace(verifiedFacts))
            {
                return null;
            }

            var systemPrompt = """
You are the customer-facing MarketLink Assistant.

Write a concise, professional, friendly answer using ONLY the VERIFIED MARKETLINK FACTS supplied below.

Strict grounding rules:
- Never invent a farmer, product, price, quantity, market, date, time, pickup slot, availability, address, favorite, or order status.
- Do not turn missing data into a negative claim. Say that MarketLink does not currently have the relevant data.
- Distinguish "listed product" from "currently available stock".
- If a farmer exists but has no active product listings, say exactly that naturally.
- If products are listed but there is no current/future inventory, explain that the listings exist but current dated stock was not found.
- Use the conversation only for wording and pronoun resolution, never as a source of business facts unless those facts are also present in VERIFIED MARKETLINK FACTS.
- Do not mention SQL, database tables, intent parsing, filters, APIs, or internal implementation.
- Do not say "according to the database".
- Keep normal answers to roughly 2-6 short sentences.
- Use bullets only when listing multiple products, markets, schedules, or pickup slots.
- End with one useful next-step question only when it genuinely helps.
- No markdown tables.

- Never claim a notification, alert, favorite, reservation, order, reminder, cancellation, subscription, or any other action was completed unless VERIFIED MARKETLINK FACTS explicitly says the backend action succeeded.
- Never say "I will notify you", "we will notify you", "I have set that up", "you are subscribed", or similar unless the verified facts confirm the persisted action.
- If an action was not executed, say clearly that it was not created.
""";

            var context = BuildConversationText(recentConversation);

            var userPrompt = $"""
USER QUESTION:
{question}

RECENT CONVERSATION:
{context}

VERIFIED MARKETLINK FACTS:
{verifiedFacts}

Write the final customer-facing answer.
""";

            try
            {
                return await SendChatAsync(
                    systemPrompt,
                    userPrompt,
                    jsonMode: false,
                    temperature: 0.25m,
                    maxTokens: 700,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Groq answer polishing failed; MarketLink will show the grounded local answer.");
                return null;
            }
        }

        private async Task<string?> SendChatAsync(
            string systemPrompt,
            string userPrompt,
            bool jsonMode,
            decimal temperature,
            int maxTokens,
            CancellationToken cancellationToken)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "chat/completions");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    ApiKey);

            var payload = new Dictionary<string, object?>
            {
                ["model"] = Model,
                ["messages"] = new object[]
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = userPrompt
                    }
                },
                ["temperature"] = temperature,
                ["max_completion_tokens"] = maxTokens
            };

            if (jsonMode)
            {
                payload["response_format"] =
                    new
                    {
                        type = "json_object"
                    };
            }

            request.Content =
                new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            var responseText =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Groq returned HTTP {StatusCode}: {Response}",
                    (int)response.StatusCode,
                    responseText);

                return null;
            }

            using var document =
                JsonDocument.Parse(responseText);

            if (!document.RootElement.TryGetProperty(
                    "choices",
                    out var choices)
                ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];

            if (!first.TryGetProperty(
                    "message",
                    out var message)
                ||
                !message.TryGetProperty(
                    "content",
                    out var content))
            {
                return null;
            }

            return content.GetString()?.Trim();
        }

        private static string BuildConversationText(
            IReadOnlyList<GroqConversationTurn> turns)
        {
            if (turns.Count == 0)
                return "(none)";

            return string.Join(
                "\n",
                turns
                    .TakeLast(8)
                    .Select(x =>
                        $"{x.Role.ToUpperInvariant()}: {x.Text}"));
        }

        private static string JoinLimited(
            IReadOnlyList<string> values,
            int max)
        {
            if (values.Count == 0)
                return "(none)";

            return string.Join(
                ", ",
                values
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(max));
        }
    }

    public sealed class GroqConversationTurn
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class GroqIntentResult
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "Unknown";

        [JsonPropertyName("farmer")]
        public string? Farmer { get; set; }

        [JsonPropertyName("market")]
        public string? Market { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("day")]
        public string? Day { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("useFavoriteFarmers")]
        public bool UseFavoriteFarmers { get; set; }

        [JsonPropertyName("useFavoriteProducts")]
        public bool UseFavoriteProducts { get; set; }

        [JsonPropertyName("useFavoriteMarkets")]
        public bool UseFavoriteMarkets { get; set; }

        [JsonPropertyName("normalizedQuery")]
        public string NormalizedQuery { get; set; } = "";
    }
}
