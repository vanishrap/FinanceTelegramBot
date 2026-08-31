using System.Net;
using System.Text;
using FinanceBot.Application;
using FinanceBot.Infrastructure.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceBot.Tests;

public sealed class OpenAiServicesTests
{
    [Fact]
    public void ReadableJson_PreservesUnicodeCharactersInLogText()
    {
        var json = ReadableJson.Serialize(new { text = "товары & услуги" });

        Assert.Contains("товары & услуги", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("&#x20;", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_ReadsOutputTextAfterReasoningItem()
    {
        const string extracted = """
            {"kind":"Expense","amount":345,"currency":"MYR","description":"Расход","merchant":null,"accountId":1,"toAccountId":null,"receipt":null,"categoryId":null,"transactionDate":null,"clarificationQuestion":null}
            """;
        var response = $$"""
            {
              "output": [
                { "type": "reasoning", "id": "reasoning-1", "summary": [] },
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    { "type": "output_text", "annotations": [], "text": {{System.Text.Json.JsonSerializer.Serialize(extracted)}} }
                  ]
                }
              ]
            }
            """;
        using var http = new HttpClient(new JsonResponseHandler(response))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        var service = new AiExtractionService(http, new FinanceOptions(), NullLogger<AiExtractionService>.Instance);

        var result = await service.ExtractAsync(new AiInput(["345 ринггит"], [], "{}"), CancellationToken.None);

        Assert.Equal(extracted, result);
    }

    [Fact]
    public async Task ExtractAsync_ThrowsClearErrorWhenOutputTextIsMissing()
    {
        using var http = new HttpClient(new JsonResponseHandler("""{"output":[{"type":"reasoning","summary":[]}]}"""))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        var service = new AiExtractionService(http, new FinanceOptions(), NullLogger<AiExtractionService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExtractAsync(new AiInput(["345 ринггит"], [], "{}"), CancellationToken.None));

        Assert.Contains("output_text", exception.Message);
    }

    private sealed class JsonResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
    }
}
