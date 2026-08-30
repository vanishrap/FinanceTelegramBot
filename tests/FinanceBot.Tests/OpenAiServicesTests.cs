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
    public async Task AnalyticsPlanAsync_TellsModelThatEnumsAreStoredAsText()
    {
        var response = """
            {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"isQuestion\":true,\"sql\":\"SELECT SUM(Amount) FROM Transactions WHERE CreatedByUserId=$userId AND Type='Expense'\",\"clarificationQuestion\":null}"}]}]}
            """;
        var handler = new JsonResponseHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var service = new AiAnalyticsService(http, new FinanceOptions(), NullLogger<AiAnalyticsService>.Instance);

        var plan = await service.PlanAsync(new AiInput(["Сколько я потратил?"], [], "{}"), CancellationToken.None);

        Assert.True(plan.IsQuestion);
        Assert.Contains("Type='Expense'", plan.Sql!);
        Assert.Contains("Transactions.Type = 'Expense'", handler.RequestBody);
        Assert.Contains("never compare Type, Direction, or Status with numeric enum values", handler.RequestBody);
    }

    [Fact]
    public async Task ExtractAsync_ReadsOutputTextAfterReasoningItem()
    {
        const string extracted = """
            {"operations":[{"kind":"Expense","amount":345,"currency":"MYR","description":"Расход","merchant":null,"accountId":1,"toAccountId":null,"receipt":null,"categoryId":null,"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null}]}
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
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
