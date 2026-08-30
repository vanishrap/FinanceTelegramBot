using System.Net;
using System.Text.Json;
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

    [Fact]
    public async Task SendRichMessageAsync_RequestsTelegramMarkdownFormatting()
    {
        var handler = new RecordingHandler("{\"ok\":true,\"result\":{}}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = new TelegramHttpClient(http, new FinanceOptions { TelegramBotToken = "123456:secret" });

        await client.SendRichMessageAsync(42, "# Итог\n**RM 100**", CancellationToken.None);

        Assert.Equal("/bot123456:secret/sendMessage", handler.RequestUri?.AbsolutePath);
        Assert.NotNull(handler.RequestBody);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(42, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("Markdown", body.RootElement.GetProperty("parse_mode").GetString());
        Assert.True(body.RootElement.GetProperty("disable_web_page_preview").GetBoolean());
    }

    private sealed class RecordingHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            };
        }
    }
}
