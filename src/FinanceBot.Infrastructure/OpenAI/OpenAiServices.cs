using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceBot.Application;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.OpenAI;
public sealed class VoiceTranscriptionService(HttpClient http, FinanceOptions options) : IVoiceTranscriptionService
{
 public async Task<string> TranscribeAsync(TelegramFile file, CancellationToken ct) { using var form = new MultipartFormDataContent(); form.Add(new StringContent(options.OpenAiTranscriptionModel), "model"); var audio = new ByteArrayContent(file.Content); audio.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType); form.Add(audio, "file", file.FileName); using var response = await http.PostAsync("audio/transcriptions", form, ct); response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct); return json.GetProperty("text").GetString() ?? ""; }
}
public sealed class AiExtractionService(HttpClient http, FinanceOptions options, ILogger<AiExtractionService> logger) : IAiExtractionService
{
 public async Task<string> ExtractAsync(AiInput input, CancellationToken ct)
 {
  var content = new List<object> { new { type="input_text", text=$"Context: {input.ContextJson}\nMessages:\n{string.Join("\n", input.Texts)}" } }; content.AddRange(input.ImageDataUrls.Select(x => (object)new { type="input_image", image_url=x }));
  var operationSchema = new { type="object", additionalProperties=false, required=new[]{"kind","amount","currency","description","merchant","accountId","toAccountId","receipt","categoryId","transactionDate","clarificationQuestion","debtDirection","counterparty","targetTransactionId","targetDebtId"}, properties=new { kind=new{type="string", @enum=new[]{"Expense","Income","Transfer","BalanceAdjustment","DebtSettlement","DebtCreate","Correction","Delete","DebtToExpense"}}, amount=new{type="number"}, currency=new{type="string"}, description=new{type="string"}, merchant=new{type=new[]{"string","null"}}, accountId=new{type=new[]{"integer","null"}}, toAccountId=new{type=new[]{"integer","null"}}, categoryId=new{type=new[]{"integer","null"}}, transactionDate=new{type=new[]{"string","null"},format="date-time"}, clarificationQuestion=new{type=new[]{"string","null"}}, debtDirection=new{type=new[]{"string","null"},@enum=new object?[]{"Payable","Receivable",null}}, counterparty=new{type=new[]{"string","null"}}, targetDebtId=new{type=new[]{"integer","null"}}, targetTransactionId=new{type=new[]{"integer","null"}}, receipt=new{type=new[]{"object","null"}, additionalProperties=false, required=new[]{"subtotal","tax","serviceCharge","discount","rounding","total","items"}, properties=new{subtotal=new{type="number"},tax=new{type="number"},serviceCharge=new{type="number"},discount=new{type="number"},rounding=new{type="number"},total=new{type="number"},items=new{type="array",items=new{type="object",additionalProperties=false,required=new[]{"name","quantity","unitPrice","baseAmount","discount","taxAllocated","serviceChargeAllocated","finalAmount","categoryId","confidence"},properties=new{name=new{type="string"},quantity=new{type="number"},unitPrice=new{type="number"},baseAmount=new{type="number"},discount=new{type="number"},taxAllocated=new{type="number"},serviceChargeAllocated=new{type="number"},finalAmount=new{type="number"},categoryId=new{type=new[]{"integer","null"}},confidence=new{type="number"}}}}}} } };
  var schema = new { type="object", additionalProperties=false, required=new[]{"operations"}, properties=new { operations=new { type="array", minItems=1, maxItems=50, items=operationSchema } } };
  var requestPayload = new { model=options.OpenAiModel, input=new object[]{new{role="system",content=(object)new[]{new{type="input_text",text="Extract every distinct financial operation from the complete conversation. One message may contain multiple expenses, income entries, transfers, or debts: return each as a separate operations array item and never combine their amounts. For a request to change an existing transaction return Correction with its exact targetTransactionId and the complete resulting transaction fields, retaining unchanged values from recentTransactions. When the user asks to record or reclassify an existing debt as an expense, return DebtToExpense with targetDebtId; copy amount, currency, description, and date from recentDebts, and do not ask for data already stored there. For deletion return Delete only when the user identifies one exact transaction ID; otherwise set targetTransactionId null and ask for the ID. Never translate broad filters, periods, categories, or merchants into Delete operations. Automatic validation feedback is application-generated: use its exact expected/actual values plus the original messages and images to correct receipt arithmetic, and always return the full corrected extraction without asking the user to calculate it. Later messages answer or correct earlier messages and take precedence. Balance statements are BalanceAdjustment, never Income. A statement that the user owes someone or someone owes the user is DebtCreate: Payable means the user owes counterparty, Receivable means counterparty owes the user; always extract counterparty and debtDirection. Description must state the actual purpose or counterparty. Select categoryId and accountId from context and never invent IDs. Categorize every positive-value receipt item independently by the product purpose, not by the merchant and not by the transaction category. Always choose the most specific leaf category: never assign a receipt item to a broad root such as Покупки merely because it came from Watsons or another mixed retailer. For example, cosmetics, personal hygiene, medicines, medical goods, household chemicals, food, and electronics from one receipt may all require different categoryIds. A technical zero-value tracking line may have null categoryId. Extract each actual transaction date/time as ISO 8601 when stated; resolve relative dates using Malaysia currentDateTime, otherwise return null. Put a clarificationQuestion only on the specific operation with a material ambiguity. Return schema JSON only."}}},new{role="user",content=(object)content}}, text=new{format=new{type="json_schema",name="finance_operations",strict=true,schema}} };
  logger.LogDebug("OpenAI request payload: {OpenAiRequest}",ReadableJson.Serialize(requestPayload));
  using var response = await http.PostAsJsonAsync("responses", requestPayload, ct); response.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
  logger.LogDebug("OpenAI response payload: {OpenAiResponse}",ReadableJson.Serialize(json.RootElement));
  return ReadOutputText(json.RootElement);
 }

 private static string ReadOutputText(JsonElement response)
 {
  if(!response.TryGetProperty("output",out var output) || output.ValueKind!=JsonValueKind.Array)
   throw new InvalidDataException("OpenAI response does not contain an output array.");

  // Responses may put reasoning and other non-message items before the final
  // assistant message. Do not assume that output[0] is the JSON text.
  foreach(var item in output.EnumerateArray())
  {
   if(!item.TryGetProperty("content",out var content) || content.ValueKind!=JsonValueKind.Array) continue;
   foreach(var part in content.EnumerateArray())
   {
    if(part.TryGetProperty("type",out var type) && type.GetString()=="output_text" &&
       part.TryGetProperty("text",out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
     return text.GetString()!;
   }
  }

  throw new InvalidDataException("OpenAI response does not contain assistant output_text.");
 }
}
