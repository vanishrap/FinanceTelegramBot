using System.Net.Http.Json;
using System.Text.Json;
using FinanceBot.Application;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.OpenAI;

public sealed class AiAnalyticsService(HttpClient http, FinanceOptions options, ILogger<AiAnalyticsService> logger) : IAiAnalyticsService
{
    private const string RichFormattingInstructions = """
        Format the answer as Telegram Rich Markdown for sendRichMessage.

        Formatting rules:
        - `# Заголовок` is the main heading.
        - `## Раздел` is a section heading.
        - Use `**жирный**` for important values and totals.
        - Use `*курсив*` for notes.
        - Use bulleted and numbered lists.
        - Use Markdown tables for comparisons and structured data. Right-align numeric columns with `---:` alignment markers.
        - Use `>` for quotes and `---` between major parts.
        - Use emoji sparingly, mostly in headings.
        - Never imitate tables with spaces.
        - Do not overload the answer with formatting.
        - For short answers, do not add unnecessary headings or tables.
        """;
    public async Task<AnalyticsPlan> PlanAsync(AiInput input, CancellationToken ct)
    {
        var schema = new { type="object", additionalProperties=false, required=new[]{"isQuestion","sql","clarificationQuestion"}, properties=new { isQuestion=new{type="boolean"}, sql=new{type=new[]{"string","null"}}, clarificationQuestion=new{type=new[]{"string","null"}} } };
        var prompt = """
            Decide whether the messages ask a question about the user's financial data, rather than describe a new transaction or debt. Questions may have any structure and can request detailed analytics by merchant, category, account, receipt item, debt, or arbitrary periods. If it is a question, produce exactly one read-only SQLite SELECT (a WITH ... SELECT is allowed). Use only the schema in context, named parameter $userId, and scope transaction/debt data to that user. Enum values are case-sensitive text: Transactions.Type is exactly Expense, Income, Transfer, BalanceAdjustment, or DebtSettlement; Categories.Type is exactly Expense or Income. Never compare them to lowercase literals. DateTimeOffset values are stored as ISO timestamps with offsets. For Malaysia calendar periods, build explicit +08:00 boundaries from context.currentDateTime and filter with datetime(TransactionDate) >= datetime(start) and datetime(TransactionDate) < datetime(end); never use date(TransactionDate), server-local time, or an inclusive 23:59:59 end. "Последний день" means the current Malaysia calendar day unless the user explicitly asks for the rolling previous 24 hours. Debt Direction=Payable means the user owes Counterparty; Receivable means Counterparty owes the user. Never use PRAGMA or modifying statements. Prefer clear column aliases. Aggregate currencies separately; never add unlike currencies. Do not generate zero-filled rows for every category unless explicitly requested; analytics should be based on matching transactions. Limit detail queries to 200 rows. Automatic query feedback means the prior SQL returned no financial data despite recent user transactions: diagnose its enum/date/join filters and return corrected SQL. If essential interpretation is ambiguous, return a concise Russian clarificationQuestion and null sql. Requests to correct/delete a transaction or reclassify a debt as an expense are mutations, not questions. If this is a transaction or debt statement, set isQuestion=false and both other fields null.
            """;
        var payload = new { model=options.OpenAiModel, input=new object[]{new{role="system",content=prompt},new{role="user",content=$"Context and database schema:\n{input.ContextJson}\nMessages:\n{string.Join("\n",input.Texts)}"}}, text=new{format=new{type="json_schema",name="analytics_plan",strict=true,schema}} };
        logger.LogDebug("OpenAI analytics planning request: {Request}", JsonSerializer.Serialize(payload));
        using var response = await http.PostAsJsonAsync("responses", payload, ct); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var output = ReadOutputText(json.RootElement);
        logger.LogDebug("OpenAI analytics plan: {Plan}", output);
        return JsonSerializer.Deserialize<AnalyticsPlan>(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Empty analytics plan.");
    }

    public async Task<string> AnswerAsync(AiInput input, AnalyticsPlan plan, string queryResultsJson, CancellationToken ct)
    {
        var prompt = $"""
            Answer the user's financial question in Russian using only the supplied query result. Preserve the original conversation context, explain periods and grouping, format money readably, mention when no data was found, and do not expose SQL unless asked. Keep the complete answer below 3500 characters so it fits in one Telegram message.

            {RichFormattingInstructions}

            Table example:
            | Категория | Сумма | Доля |
            |:----------|------:|-----:|
            | Еда | RM 820 | 31% |
            | Такси | RM 210 | 8% |
            """;
        var payload = new { model=options.OpenAiModel, input=new object[]{new{role="system",content=prompt},new{role="user",content=$"Original context:\n{input.ContextJson}\nOriginal messages:\n{string.Join("\n",input.Texts)}\nExecuted SQL:\n{plan.Sql}\nDatabase result JSON:\n{queryResultsJson}"}} };
        using var response = await http.PostAsJsonAsync("responses", payload, ct); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return ReadOutputText(json.RootElement);
    }

    private static string ReadOutputText(JsonElement response)
    {
        if (response.TryGetProperty("output", out var output)) foreach (var item in output.EnumerateArray())
            if (item.TryGetProperty("content", out var content)) foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("type", out var type) && type.GetString()=="output_text" && part.TryGetProperty("text", out var text)) return text.GetString() ?? "";
        throw new InvalidDataException("OpenAI response does not contain assistant output_text.");
    }
}
