using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceBot.Tests;

public sealed class BatchProcessorTests
{
    [Fact]
    public async Task ProcessDueAsync_UsesFallbackAccountAndReplies_WhenAiDoesNotSelectAccount()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-fallback-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (_, categoryId) = await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":48,"currency":"MYR","description":"Поездка Grab","merchant":"Grab","accountId":null,"toAccountId":null,"receipt":null,"categoryId":{{categoryId}}}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction,
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var transaction = await db.Transactions.Include(x => x.Movements).ThenInclude(x => x.Account).SingleAsync();
            Assert.Equal(-48m, Assert.Single(transaction.Movements).Amount);
            Assert.Equal("Не указан", transaction.Movements[0].Account.Name);
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("Транзакция записана", reply);
            Assert.Contains("Сумма: 48.00 MYR", reply);
            Assert.Contains("Счёт: Не указан", reply);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ProcessDueAsync_PersistsCategoryAndSendsDetailedConfirmation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-processor-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, categoryId) = await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":25.50,"currency":"MYR","description":"Поездка домой","merchant":"Такси","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{categoryId}}}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction,
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var transaction = await db.Transactions.Include(x => x.Category).SingleAsync();
            Assert.Equal(categoryId, transaction.CategoryId);
            Assert.Equal(25.50m, transaction.Amount);
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("Транзакция записана", reply);
            Assert.Contains("Сумма: 25.50 MYR", reply);
            Assert.Contains("Категория: Транспорт → Такси", reply);
            Assert.Contains("Счёт: Наличные", reply);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<(long AccountId, long CategoryId)> SeedAsync(IDbContextFactory<FinanceDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        var user = new User { TelegramUserId = 42, Name = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var account = new Account { Name = "Наличные", CurrencyCode = "MYR", Type = AccountType.Cash, CreatedAt = DateTimeOffset.UtcNow };
        var category = await db.Categories.SingleAsync(x => x.Name == "Такси");
        db.Accounts.Add(account);
        db.InputBatches.Add(new InputBatch
        {
            User = user,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            LastMessageAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = BatchStatus.Collecting,
            Messages = [new InputMessage { TelegramMessageId = 101, Type = InputMessageType.Text, Text = "Такси 25.50", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) }]
        });
        await db.SaveChangesAsync();
        return (account.Id, category.Id);
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        public FinanceDbContext CreateDbContext() => new(options);
    }

    private sealed class RecordingTelegramClient : ITelegramClient
    {
        public List<string> SentMessages { get; } = [];
        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct) => Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
        public Task<TelegramFile> DownloadAsync(string fileId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendAsync(long chatId, string text, CancellationToken ct) { SentMessages.Add(text); return Task.CompletedTask; }
    }

    private sealed class StubExtractionService(string output) : IAiExtractionService
    {
        public Task<string> ExtractAsync(AiInput input, CancellationToken ct) => Task.FromResult(output);
    }

    private sealed class StubTranscriptionService : IVoiceTranscriptionService
    {
        public Task<string> TranscribeAsync(TelegramFile file, CancellationToken ct) => throw new NotSupportedException();
    }
}
