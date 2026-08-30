using System.Net.Http.Json;
using System.Text.Json;
using FinanceBot.Application;

namespace FinanceBot.Infrastructure.Telegram;
public sealed class TelegramHttpClient(HttpClient http, FinanceOptions options) : ITelegramClient
{
    private string Url(string method) => $"/bot{options.TelegramBotToken}/{method}";
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        using var response = await http.GetAsync(Url($"getUpdates?timeout=25&offset={offset}&allowed_updates=%5B%22message%22%5D"), ct); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct)); var list = new List<TelegramUpdate>();
        foreach (var u in json.RootElement.GetProperty("result").EnumerateArray())
        {
            if (!u.TryGetProperty("message", out var m) || !m.TryGetProperty("from", out var from)) continue;
            string? photo = null; if (m.TryGetProperty("photo", out var photos)) photo = photos.EnumerateArray().Last().GetProperty("file_id").GetString();
            string? voice = m.TryGetProperty("voice", out var v) ? v.GetProperty("file_id").GetString() : null;
            list.Add(new(u.GetProperty("update_id").GetInt64(), m.GetProperty("message_id").GetInt64(), from.GetProperty("id").GetInt64(), from.TryGetProperty("first_name", out var n) ? n.GetString() ?? "User" : "User", m.TryGetProperty("text", out var t) ? t.GetString() : m.TryGetProperty("caption", out var c) ? c.GetString() : null, photo, voice));
        }
        return list;
    }
    public async Task<TelegramFile> DownloadAsync(string fileId, CancellationToken ct)
    {
        var metadata = await http.GetFromJsonAsync<JsonElement>(Url($"getFile?file_id={Uri.EscapeDataString(fileId)}"), ct); var path = metadata.GetProperty("result").GetProperty("file_path").GetString()!;
        var bytes = await http.GetByteArrayAsync($"/file/bot{options.TelegramBotToken}/{path}", ct); return new(bytes, Path.GetFileName(path), path.EndsWith(".oga", StringComparison.OrdinalIgnoreCase) ? "audio/ogg" : "image/jpeg");
    }
    public async Task SendRichMessageAsync(long chatId, string text, CancellationToken ct)
    {
        // Telegram exposes rich text through sendMessage + parse_mode. Keep the
        // richer Markdown source intact: unsupported constructs such as headings
        // and tables remain readable plain text, while emphasis is rendered.
        using var response = await http.PostAsJsonAsync(Url("sendMessage"), new
        {
            chat_id = chatId,
            text,
            parse_mode = "Markdown",
            disable_web_page_preview = true
        }, ct);
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode != System.Net.HttpStatusCode.BadRequest) response.EnsureSuccessStatusCode();

        // AI output can occasionally contain malformed Markdown. Never lose the
        // financial answer because of a Telegram entity parsing error.
        using var fallback = await http.PostAsJsonAsync(Url("sendMessage"), new { chat_id = chatId, text }, ct);
        fallback.EnsureSuccessStatusCode();
    }
}
