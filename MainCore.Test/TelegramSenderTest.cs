using System.Net;

using MainCore.Services;

namespace MainCore.Test
{
    public sealed class TelegramSenderTest
    {
        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public string Body { get; private set; } = "";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                return respond(request);
            }
        }

        private static TelegramCredentials Credentials => new() { BotToken = "123:fake-token", ChatId = "456" };

        [Fact]
        public async Task SendsPlainTextAndChecksTelegramAcknowledgement()
        {
            var handler = new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(Credentials, "badge 0 -> 1", CancellationToken.None);
            result.Success.ShouldBeTrue();
            handler.Body.ShouldContain("\"chat_id\":\"456\"");
            handler.Body.ShouldNotContain("parse_mode");
        }

        [Theory]
        [InlineData(429, "{\"ok\":false,\"parameters\":{\"retry_after\":42}}", true, 42)]
        [InlineData(500, "{\"ok\":false}", true, 0)]
        [InlineData(401, "{\"ok\":false}", false, 0)]
        [InlineData(200, "{\"ok\":false,\"error_code\":403}", false, 0)]
        public async Task ClassifiesApiFailures(int status, string body, bool retryable, int delay)
        {
            var handler = new StubHandler(_ => new((HttpStatusCode)status) { Content = new StringContent(body) });
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(Credentials, "test", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Retryable.ShouldBe(retryable);
            if (delay > 0) result.RetryAfter.ShouldBe(TimeSpan.FromSeconds(delay));
        }

        [Fact]
        public async Task NetworkErrorsDoNotExposeToken()
        {
            var handler = new StubHandler(request => throw new HttpRequestException(request.RequestUri!.ToString()));
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(Credentials, "test", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Retryable.ShouldBeTrue();
            result.Error.ShouldNotContain(Credentials.BotToken);
        }

        [Fact]
        public async Task InvalidCredentialsDoNotMakeRequest()
        {
            var handler = new StubHandler(_ => throw new InvalidOperationException("Should not send"));
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(new TelegramCredentials(), "test", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Retryable.ShouldBeFalse();
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{\"ok\":false,\"parameters\":\"unexpected\",\"error_code\":\"bad\"}")]
        [InlineData("not json")]
        public async Task HandlesUnexpectedResponseShape(string body)
        {
            var handler = new StubHandler(_ => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(body) });
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(Credentials, "test", CancellationToken.None);
            result.Success.ShouldBeFalse();
            result.Retryable.ShouldBeTrue();
        }

        [Fact]
        public async Task RespectsRetryHeaderEvenWhenResponseIsNotJson()
        {
            var handler = new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("busy") };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                return response;
            });
            using var sender = new TelegramSender(new HttpClient(handler));
            var result = await sender.Send(Credentials, "test", CancellationToken.None);
            result.RetryAfter.ShouldBe(TimeSpan.FromSeconds(60));
        }

        [Fact]
        public async Task CancellationIsNotTurnedIntoDeliveryFailure()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var handler = new StubHandler(_ => throw new OperationCanceledException(cancellation.Token));
            using var sender = new TelegramSender(new HttpClient(handler));
            await Should.ThrowAsync<OperationCanceledException>(() => sender.Send(Credentials, "test", cancellation.Token));
        }
    }
}
