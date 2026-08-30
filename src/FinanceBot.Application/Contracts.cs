using FinanceBot.Domain;

namespace FinanceBot.Application;

public sealed record FinanceOptions
{
    public string TelegramBotToken { get; init; } = "";
    public string OpenAiApiKey { get; init; } = "";
    public HashSet<long> AllowedTelegramUserIds { get; init; } = [];
    public string OpenAiModel { get; init; } = "gpt-4.1-mini";
    public string OpenAiTranscriptionModel { get; init; } = "gpt-4o-mini-transcribe";
    public int MessageBatchDelaySeconds { get; init; } = 5;
    public string DatabasePath { get; init; } = "/data/finance.db";
    public string DefaultCurrency { get; init; } = "MYR";
    public decimal ValidationRoundingTolerance { get; init; } = .05m;
}
public sealed record TelegramUpdate(long UpdateId, long MessageId, long UserId, string UserName, string? Text, string? PhotoFileId, string? VoiceFileId);
public sealed record TelegramFile(byte[] Content, string FileName, string ContentType);
public interface ITelegramClient { Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct); Task<TelegramFile> DownloadAsync(string fileId, CancellationToken ct); Task SendAsync(long chatId, string text, CancellationToken ct); }
public interface IVoiceTranscriptionService { Task<string> TranscribeAsync(TelegramFile file, CancellationToken ct); }
public interface IAiExtractionService { Task<string> ExtractAsync(AiInput input, CancellationToken ct); }
public sealed record AiInput(IReadOnlyList<string> Texts, IReadOnlyList<string> ImageDataUrls, string ContextJson);
public sealed record ExtractedOperation(string Kind, decimal Amount, string Currency, string Description, string? Merchant, long? AccountId, long? ToAccountId, ReceiptDraft? Receipt, long? CategoryId = null);
public sealed record ReceiptDraft(decimal Subtotal, decimal Tax, decimal ServiceCharge, decimal Discount, decimal Rounding, decimal Total, IReadOnlyList<ReceiptItemDraft> Items);
public sealed record ReceiptItemDraft(string Name, decimal Quantity, decimal UnitPrice, decimal BaseAmount, decimal Discount, decimal TaxAllocated, decimal ServiceChargeAllocated, decimal FinalAmount, long? CategoryId, decimal Confidence);
public sealed record ValidationCheck(string Type, decimal Expected, decimal Actual, bool Passed, string Message);
