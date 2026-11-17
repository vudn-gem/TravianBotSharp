using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MainCore.Services
{
    public sealed class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long Id { get; set; } = -1;
        [JsonPropertyName("message")]
        public TelegramCommandMessage? Message { get; set; }
    }

    public sealed class TelegramCommandMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("date")]
        public long Date { get; set; }
        [JsonPropertyName("chat")]
        public TelegramCommandChat? Chat { get; set; }
        [JsonPropertyName("from")]
        public TelegramCommandUser? From { get; set; }
        [JsonPropertyName("forward_origin")]
        public object? ForwardOrigin { get; set; }
        [JsonPropertyName("forward_date")]
        public long? ForwardDate { get; set; }
        [JsonPropertyName("via_bot")]
        public object? ViaBot { get; set; }

        public bool IsAuthorized(TelegramCredentials credentials) =>
            credentials.CanReceiveCommands && Chat?.Type == "private" &&
            long.TryParse(credentials.ChatId, NumberStyles.None, CultureInfo.InvariantCulture, out var chatId) && Chat.Id == chatId &&
            From is { IsBot: false } && From.Id == Chat.Id &&
            ForwardOrigin is null && ForwardDate is null && ViaBot is null;
    }

    public sealed class TelegramCommandChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";
    }

    public sealed class TelegramCommandUser
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("is_bot")]
        public bool IsBot { get; set; }
    }

    public sealed class TelegramUpdateBatch
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }
        [JsonPropertyName("result")]
        public List<TelegramUpdate>? Updates { get; set; }
        [JsonPropertyName("error_code")]
        public int ErrorCode { get; set; }
        [JsonPropertyName("parameters")]
        public TelegramPollingParameters? Parameters { get; set; }
    }

    public sealed class TelegramPollingParameters
    {
        [JsonPropertyName("retry_after")]
        public int RetryAfter { get; set; }
    }

    public sealed class TelegramCommandClient(HttpClient client) : IDisposable
    {
        public async Task<TelegramUpdateBatch> Poll(TelegramCredentials credentials, long offset, CancellationToken cancellationToken)
        {
            if (!credentials.CanReceiveCommands) throw new InvalidOperationException("Telegram commands are disabled or private chat is not configured.");
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{credentials.BotToken}/getUpdates",
                new { offset, timeout = offset < 0 ? 0 : 25, limit = 100, allowed_updates = new[] { "message" } },
                cancellationToken);
            var batch = await response.Content.ReadFromJsonAsync<TelegramUpdateBatch>(cancellationToken);
            if (batch is null) throw new System.Text.Json.JsonException("Empty Telegram polling response.");
            if (!response.IsSuccessStatusCode)
            {
                batch.Ok = false;
                batch.ErrorCode = (int)response.StatusCode;
            }
            if (batch.Ok && (batch.Updates is null || batch.Updates.Any(x => x is null || x.Id < 0 || x.Id == long.MaxValue)))
                throw new System.Text.Json.JsonException("Invalid Telegram updates.");
            return batch;
        }

        public void Dispose() => client.Dispose();
    }
}
