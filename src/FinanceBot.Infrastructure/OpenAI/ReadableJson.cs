using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinanceBot.Infrastructure.OpenAI;

public static class ReadableJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // JSON's default encoder turns Cyrillic into \uXXXX sequences. That is
        // valid JSON but unreadable in diagnostic logs, even in a UTF-8 file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
