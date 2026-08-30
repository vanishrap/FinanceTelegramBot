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
  var schema = new { type="object", additionalProperties=false, required=new[]{"kind","amount","currency","description","merchant","accountId","toAccountId","receipt","categoryId","transactionDate","clarificationQuestion"}, properties=new { kind=new{type="string", @enum=new[]{"Expense","Income","Transfer","BalanceAdjustment","DebtSettlement","DebtCreate","Correction"}}, amount=new{type="number"}, currency=new{type="string"}, description=new{type="string"}, merchant=new{type=new[]{"string","null"}}, accountId=new{type=new[]{"integer","null"}}, toAccountId=new{type=new[]{"integer","null"}}, categoryId=new{type=new[]{"integer","null"}}, transactionDate=new{type=new[]{"string","null"},format="date-time"}, clarificationQuestion=new{type=new[]{"string","null"}}, receipt=new{type=new[]{"object","null"}, additionalProperties=false, required=new[]{"subtotal","tax","serviceCharge","discount","rounding","total","items"}, properties=new{subtotal=new{type="number"},tax=new{type="number"},serviceCharge=new{type="number"},discount=new{type="number"},rounding=new{type="number"},total=new{type="number"},items=new{type="array",items=new{type="object",additionalProperties=false,required=new[]{"name","quantity","unitPrice","baseAmount","discount","taxAllocated","serviceChargeAllocated","finalAmount","categoryId","confidence"},properties=new{name=new{type="string"},quantity=new{type="number"},unitPrice=new{type="number"},baseAmount=new{type="number"},discount=new{type="number"},taxAllocated=new{type="number"},serviceChargeAllocated=new{type="number"},finalAmount=new{type="number"},categoryId=new{type=new[]{"integer","null"}},confidence=new{type="number"}}}}}} } };
  var requestPayload = new { model=options.OpenAiModel, input=new object[]{new{role="system",content=(object)new[]{new{type="input_text",text="Extract one financial operation from the complete conversation. Later messages answer or correct earlier messages and take precedence. Balance statements are BalanceAdjustment, never Income. Description must state the actual purpose or counterparty; never replace a missing purpose with generic words such as Expense, Расход, Purchase, or Трата. Select the most specific matching categoryId and an accountId from context. Never invent IDs. Extract the actual transaction date/time as ISO 8601 when stated; resolve relative dates using message timestamps supplied in context, otherwise return null. If any material detail is missing, ambiguous, or contradictory (including what the payment was for, amount, currency, type, date, account, category, or receipt item), set clarificationQuestion to one concise Russian question explaining exactly what the user must clarify; otherwise null. Return schema JSON only."}}},new{role="user",content=(object)content}}, text=new{format=new{type="json_schema",name="finance_operation",strict=true,schema}} };
  logger.LogDebug("OpenAI request payload: {OpenAiRequest}",JsonSerializer.Serialize(requestPayload));
  using var response = await http.PostAsJsonAsync("responses", requestPayload, ct); response.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
  logger.LogDebug("OpenAI response payload: {OpenAiResponse}",json.RootElement.GetRawText());
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
