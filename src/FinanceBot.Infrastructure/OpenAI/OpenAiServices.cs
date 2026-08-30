using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceBot.Application;

namespace FinanceBot.Infrastructure.OpenAI;
public sealed class VoiceTranscriptionService(HttpClient http, FinanceOptions options) : IVoiceTranscriptionService
{
 public async Task<string> TranscribeAsync(TelegramFile file, CancellationToken ct) { using var form = new MultipartFormDataContent(); form.Add(new StringContent(options.OpenAiTranscriptionModel), "model"); var audio = new ByteArrayContent(file.Content); audio.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType); form.Add(audio, "file", file.FileName); using var response = await http.PostAsync("audio/transcriptions", form, ct); response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct); return json.GetProperty("text").GetString() ?? ""; }
}
public sealed class AiExtractionService(HttpClient http, FinanceOptions options) : IAiExtractionService
{
 public async Task<string> ExtractAsync(AiInput input, CancellationToken ct)
 {
  var content = new List<object> { new { type="input_text", text=$"Context: {input.ContextJson}\nMessages:\n{string.Join("\n", input.Texts)}" } }; content.AddRange(input.ImageDataUrls.Select(x => (object)new { type="input_image", image_url=x }));
  var schema = new { type="object", additionalProperties=false, required=new[]{"kind","amount","currency","description","merchant","accountId","toAccountId","receipt"}, properties=new { kind=new{type="string", @enum=new[]{"Expense","Income","Transfer","BalanceAdjustment","DebtSettlement","DebtCreate","Correction"}}, amount=new{type="number"}, currency=new{type="string"}, description=new{type="string"}, merchant=new{type=new[]{"string","null"}}, accountId=new{type=new[]{"integer","null"}}, toAccountId=new{type=new[]{"integer","null"}}, receipt=new{type=new[]{"object","null"}, additionalProperties=false, required=new[]{"subtotal","tax","serviceCharge","discount","rounding","total","items"}, properties=new{subtotal=new{type="number"},tax=new{type="number"},serviceCharge=new{type="number"},discount=new{type="number"},rounding=new{type="number"},total=new{type="number"},items=new{type="array",items=new{type="object",additionalProperties=false,required=new[]{"name","quantity","unitPrice","baseAmount","discount","taxAllocated","serviceChargeAllocated","finalAmount","categoryId","confidence"},properties=new{name=new{type="string"},quantity=new{type="number"},unitPrice=new{type="number"},baseAmount=new{type="number"},discount=new{type="number"},taxAllocated=new{type="number"},serviceChargeAllocated=new{type="number"},finalAmount=new{type="number"},categoryId=new{type=new[]{"integer","null"}},confidence=new{type="number"}}}}}} } };
  using var response = await http.PostAsJsonAsync("responses", new { model=options.OpenAiModel, input=new object[]{new{role="system",content=(object)new[]{new{type="input_text",text="Extract one financial operation. Balance statements are BalanceAdjustment, never Income. Never invent account/category IDs. Return schema JSON only."}}},new{role="user",content=(object)content}}, text=new{format=new{type="json_schema",name="finance_operation",strict=true,schema}} }, ct); response.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct)); return json.RootElement.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
 }
}
