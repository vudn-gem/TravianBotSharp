using System.Net;
using System.Text.Json;

using MainCore.Services;

namespace MainCore.Test
{
    public sealed class TelegramCommandClientTest
    {
        private static TelegramCredentials Credentials => new()
        {
            BotToken = "123:fake-token", ChatId = "456", EnableCommands = true,
        };

        [Fact]
        public async Task PollsOnlyMessagesWithOffsetAndLongTimeoutAndParsesPrivateSender()
        {
            var handler = new StubHandler("""
                {"ok":true,"result":[
                    {"update_id":42,"message":{"date":1794902400,"text":"/hero-farm 1 10",
                    "chat":{"id":456,"type":"private"},"from":{"id":456,"is_bot":false}}},
                    {"update_id":43,"edited_message":{"text":"/start-farm 1"}}
                ]}
                """);
            using var client = new TelegramCommandClient(new HttpClient(handler));
            var batch = await client.Poll(Credentials, 42, CancellationToken.None);
            batch.Ok.ShouldBeTrue();
            batch.Updates.ShouldNotBeNull();
            batch.Updates.Count.ShouldBe(2);
            batch.Updates[0].Id.ShouldBe(42);
            batch.Updates[0].Message!.IsAuthorized(Credentials).ShouldBeTrue();
            batch.Updates[0].Message!.Text.ShouldBe("/hero-farm 1 10");
            batch.Updates[1].Message.ShouldBeNull();
            handler.Path.ShouldEndWith("/getUpdates");
            using var body = JsonDocument.Parse(handler.Body);
            body.RootElement.GetProperty("offset").GetInt64().ShouldBe(42);
            body.RootElement.GetProperty("timeout").GetInt32().ShouldBe(25);
            body.RootElement.GetProperty("allowed_updates")[0].GetString().ShouldBe("message");
        }

        [Fact]
        public async Task BootstrapUsesNegativeOffsetWithoutLongPolling()
        {
            var handler = new StubHandler("""{"ok":true,"result":[]}""");
            using var client = new TelegramCommandClient(new HttpClient(handler));
            await client.Poll(Credentials, -1, CancellationToken.None);
            using var body = JsonDocument.Parse(handler.Body);
            body.RootElement.GetProperty("offset").GetInt64().ShouldBe(-1);
            body.RootElement.GetProperty("timeout").GetInt32().ShouldBe(0);
        }

        [Theory]
        [InlineData(429, """{"ok":false,"parameters":{"retry_after":42}}""", 42)]
        [InlineData(409, """{"ok":false}""", 0)]
        public async Task ReturnsPollingFailureAndRetryDelay(int status, string json, int seconds)
        {
            using var client = new TelegramCommandClient(new HttpClient(new StubHandler(json, (HttpStatusCode)status)));
            var batch = await client.Poll(Credentials, 1, CancellationToken.None);
            batch.Ok.ShouldBeFalse();
            batch.ErrorCode.ShouldBe(status);
            (batch.Parameters?.RetryAfter ?? 0).ShouldBe(seconds);
        }

        [Fact]
        public async Task DisabledCommandsMakeNoNetworkRequest()
        {
            var handler = new StubHandler("""{"ok":true,"result":[]}""");
            using var client = new TelegramCommandClient(new HttpClient(handler));
            var credentials = Credentials;
            credentials.EnableCommands = false;
            await Should.ThrowAsync<InvalidOperationException>(() => client.Poll(credentials, 0, CancellationToken.None));
            handler.Body.ShouldBe("");
        }

        [Theory]
        [InlineData("""{"ok":true,"result":[null]}""")]
        [InlineData("""{"ok":true,"result":[{"message":{"text":"/start-farm 1"}}]}""")]
        [InlineData("""{"ok":true}""")]
        [InlineData("null")]
        public async Task RejectsMalformedUpdates(string json)
        {
            using var client = new TelegramCommandClient(new HttpClient(new StubHandler(json)));
            await Should.ThrowAsync<JsonException>(() => client.Poll(Credentials, 1, CancellationToken.None));
        }

        private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
        {
            public string Body { get; private set; } = "";
            public string Path { get; private set; } = "";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Path = request.RequestUri!.AbsolutePath;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
