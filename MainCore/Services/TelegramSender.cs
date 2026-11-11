using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MainCore.Services
{
    public sealed record TelegramSendResult(bool Success, bool Retryable, string Error = "", TimeSpan? RetryAfter = null);

    public sealed class TelegramSender(HttpClient client) : IDisposable
    {
        public async Task<TelegramSendResult> Send(
            TelegramCredentials credentials, string text, CancellationToken cancellationToken)
        {
            if (!credentials.IsValid)
            {
                return new(false, false, "Configure a valid Telegram bot token and numeric chat ID.");
            }

            try
            {
                using var response = await client.PostAsJsonAsync(
                    $"https://api.telegram.org/bot{credentials.BotToken}/sendMessage",
                    new { chat_id = credentials.ChatId, text },
                    cancellationToken);
                var status = (int)response.StatusCode;
                TimeSpan? delay = response.Headers.RetryAfter?.Delta;
                if (response.Headers.RetryAfter?.Date is DateTimeOffset retryDate)
                {
                    delay = retryDate > DateTimeOffset.UtcNow ? retryDate - DateTimeOffset.UtcNow : TimeSpan.Zero;
                }
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(json);
                }
                catch (JsonException)
                {
                    return new(false, response.IsSuccessStatusCode || status == 429 || status >= 500,
                        $"Telegram returned an invalid response (status {status}).", delay);
                }
                using var responseDocument = document;
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new(false, true, "Telegram returned an invalid response.", delay);
                }
                if (response.IsSuccessStatusCode &&
                    root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                {
                    return new(true, false);
                }

                if (root.TryGetProperty("error_code", out var errorCode) &&
                    errorCode.ValueKind == JsonValueKind.Number && errorCode.TryGetInt32(out var apiStatus))
                {
                    status = apiStatus;
                }

                if (root.TryGetProperty("parameters", out var parameters) &&
                    parameters.ValueKind == JsonValueKind.Object &&
                    parameters.TryGetProperty("retry_after", out var retryAfter) &&
                    retryAfter.ValueKind == JsonValueKind.Number &&
                    retryAfter.TryGetInt32(out var seconds) && seconds >= 0)
                {
                    delay = TimeSpan.FromSeconds(seconds);
                }

                return new(false, status == (int)HttpStatusCode.TooManyRequests || status >= 500,
                    $"Telegram rejected the message (status {status}).", delay);
            }
            catch (HttpRequestException)
            {
                // Exception messages can contain the request URI, including the bot token.
                return new(false, true, "Unable to connect to Telegram.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, true, "Telegram request timed out.");
            }
        }

        public void Dispose() => client.Dispose();
    }
}
