using System.Net;
using FinanceBot.Application;
using FinanceBot.Infrastructure.Telegram;
using Xunit;

namespace FinanceBot.Tests;

public sealed class TelegramHttpClientTests
{
    [Fact]
    public async Task GetUpdatesAsync_BuildsHttpsTelegramApiUrl_WhenTokenContainsColon()
    {
        var handler = new RecordingHandler("{\"ok\":true,\"result\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = new TelegramHttpClient(http, new FinanceOptions { TelegramBotToken = "123456:secret" });

        await client.GetUpdatesAsync(0, CancellationToken.None);

        Assert.NotNull(handler.RequestUri);
        Assert.Equal("https", handler.RequestUri.Scheme);
        Assert.Equal("api.telegram.org", handler.RequestUri.Host);
        Assert.Equal("/bot123456:secret/getUpdates", handler.RequestUri.AbsolutePath);
    }

    private sealed class RecordingHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}
