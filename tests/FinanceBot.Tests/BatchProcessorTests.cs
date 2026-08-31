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
    public async Task ProcessDueAsync_RecordsMultipleExpensesFromOneMessage()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-multiple-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, taxiCategoryId) = await SeedAsync(factory);
            long restaurantCategoryId;
            await using (var db = factory.CreateDbContext()) restaurantCategoryId = (await db.Categories.SingleAsync(x => x.Name == "Рестораны и кафе")).Id;
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"operations":[
                  {"kind":"Expense","amount":35,"currency":"MYR","description":"Поездка на такси","merchant":"Такси","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{taxiCategoryId}},"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null},
                  {"kind":"Expense","amount":19,"currency":"MYR","description":"Еда в ресторане","merchant":"Dodo Korea","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{restaurantCategoryId}},"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null}
                ]}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var resultDb = factory.CreateDbContext();
            var transactions = await resultDb.Transactions.OrderBy(x => x.Amount).ToListAsync();
            Assert.Equal(2, transactions.Count);
            Assert.Equal(new[] { 19m, 35m }, transactions.Select(x => x.Amount));
            Assert.All(transactions, x => Assert.NotNull(x.InputBatchId));
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("Записано операций: 2", reply);
            Assert.Contains("Dodo Korea", reply);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_UsesCurrentMalaysiaTime_WhenTransactionDateIsMissing()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-malaysia-time-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, categoryId) = await SeedAsync(factory);
            var before = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":25.50,"currency":"MYR","description":"Поездка домой","merchant":"Такси","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{categoryId}},"transactionDate":null,"clarificationQuestion":null}
                """);
            var processor = new BatchProcessor(factory, new RecordingTelegramClient(), new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var transaction = await db.Transactions.SingleAsync();
            var after = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
            Assert.Equal(TimeSpan.FromHours(8), transaction.TransactionDate.Offset);
            Assert.InRange(transaction.TransactionDate, before, after);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_AutomaticallyCorrectsReceiptArithmeticWithOriginalContext()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-receipt-retry-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, categoryId) = await SeedAsync(factory);
            long shoppingCategoryId;
            await using (var db = factory.CreateDbContext()) shoppingCategoryId = (await db.Categories.SingleAsync(x => x.Name == "Покупки")).Id;
            string Output(decimal tax, long itemCategoryId) => $$"""
                {"kind":"Expense","amount":12,"currency":"MYR","description":"Обед","merchant":"Cafe","accountId":{{accountId}},"toAccountId":null,"receipt":{"subtotal":10,"tax":{{tax}},"serviceCharge":0,"discount":0,"rounding":0,"total":12,"items":[{"name":"Обед","quantity":1,"unitPrice":12,"baseAmount":10,"discount":0,"taxAllocated":{{tax}},"serviceChargeAllocated":0,"finalAmount":12,"categoryId":{{itemCategoryId}},"confidence":0.9}]},"categoryId":{{categoryId}},"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null,"targetTransactionId":null,"targetDebtId":null}
                """;
            var extraction = new SequencedExtractionService(Output(1, shoppingCategoryId), Output(2, categoryId));
            var processor = new BatchProcessor(factory, new RecordingTelegramClient(), new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            Assert.Equal(12m, (await db.Transactions.SingleAsync()).Amount);
            Assert.Equal(2, extraction.Inputs.Count);
            Assert.Contains("Такси 25.50", extraction.Inputs[1].Texts[0]);
            Assert.Contains("AUTOMATIC VALIDATION FEEDBACK", extraction.Inputs[1].Texts[^1]);
            Assert.Contains("ReceiptTotal", extraction.Inputs[1].Texts[^1]);
            Assert.Contains("ReceiptItemCategory", extraction.Inputs[1].Texts[^1]);
            Assert.Single(await db.ValidationResults.Where(x => x.Type == "ReceiptTotal").ToListAsync());
            Assert.Equal(ValidationStatus.Pass, (await db.ValidationResults.SingleAsync(x => x.Type == "ReceiptTotal")).Status);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_ReplansEmptyAnalyticsQuery_WhenRecentTransactionsExist()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-analytics-replan-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            await SeedAsync(factory);
            await using (var db = factory.CreateDbContext())
            {
                var userId = (await db.Users.SingleAsync()).Id;
                var transaction = new Transaction { CreatedByUserId = userId, Type = TransactionType.Expense, TransactionDate = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)), CurrencyCode = "MYR", Amount = 130.50m, Description = "Mercato", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
                db.Receipts.Add(new Receipt { Transaction=transaction,MerchantName="Mercato",ReceiptDate=transaction.TransactionDate,Subtotal=130.50m,Total=130.50m,CurrencyCode="MYR",Items=[new ReceiptItem{RawName="Milk",NormalizedName="Milk",Quantity=1,UnitPrice=130.50m,BaseAmount=130.50m,FinalAmount=130.50m,AiConfidence=1}] });
                await db.SaveChangesAsync();
            }
            var analytics = new SequencedAnalyticsService(
                new AnalyticsPlan(true, "SELECT SUM(Amount) AS TotalAmount, COUNT(*) AS OperationCount FROM Transactions WHERE Type='expense'", null),
                new AnalyticsPlan(true, "SELECT SUM(Amount) AS TotalAmount, COUNT(*) AS OperationCount FROM Transactions WHERE Type='Expense'", null));
            var telegram = new RecordingTelegramClient();
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), new StubExtractionService("{}"), analytics, new AnalyticsQueryExecutor(factory), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            Assert.Equal(2, analytics.PlanInputs.Count);
            Assert.Contains("recentReceipts", analytics.PlanInputs[0].ContextJson);
            Assert.Contains("recentReceiptItems", analytics.PlanInputs[0].ContextJson);
            Assert.Contains("Milk", analytics.PlanInputs[0].ContextJson);
            Assert.Contains("AUTOMATIC QUERY FEEDBACK", analytics.PlanInputs[1].Texts[^1]);
            var answer = Assert.Single(telegram.SentMessages);
            Assert.Contains("130.5", answer);
            Assert.Contains("OperationCount", answer);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_RecordsWhoOwesWhom()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-debt-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService("""
                {"kind":"DebtCreate","amount":300,"currency":"MYR","description":"Одолжил на ремонт","merchant":null,"accountId":null,"toAccountId":null,"receipt":null,"categoryId":null,"transactionDate":null,"clarificationQuestion":null,"debtDirection":"Receivable","counterparty":"Иван"}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var debt = await db.Debts.SingleAsync();
            Assert.Equal(DebtDirection.Receivable, debt.Direction);
            Assert.Equal("Иван", debt.Counterparty);
            Assert.Equal(300m, debt.OriginalAmount);
            Assert.NotNull(debt.InputBatchId);
            Assert.Contains("Вам должен", Assert.Single(telegram.SentMessages));
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_AsksWhatExpenseWasFor_WhenOnlyAmountWasProvided()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-purpose-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, _) = await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":457,"currency":"MYR","description":"Расход","merchant":null,"accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":null,"transactionDate":null,"clarificationQuestion":null}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            Assert.Empty(await db.Transactions.ToListAsync());
            Assert.Equal(BatchStatus.NeedsReview, (await db.InputBatches.SingleAsync()).Status);
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("на что именно", reply, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("категорию", reply, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ProcessDueAsync_AsksForAccountAndKeepsBatch_WhenAiDoesNotSelectAccount()
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
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            Assert.Empty(await db.Transactions.ToListAsync());
            Assert.Equal(BatchStatus.NeedsReview, (await db.InputBatches.SingleAsync()).Status);
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("не могу записать", reply);
            Assert.Contains("счёт", reply, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ответным сообщением", reply);
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
            var transactionDate = new DateTimeOffset(2026, 8, 27, 18, 30, 0, TimeSpan.FromHours(8));
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":25.50,"currency":"MYR","description":"Поездка домой","merchant":"Такси","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{categoryId}},"transactionDate":"{{transactionDate:O}}","clarificationQuestion":null}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(),
                new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var db = factory.CreateDbContext();
            var transaction = await db.Transactions.Include(x => x.Category).SingleAsync();
            Assert.Equal(categoryId, transaction.CategoryId);
            Assert.Equal(25.50m, transaction.Amount);
            Assert.Equal(transactionDate, transaction.TransactionDate);
            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("Транзакция записана", reply);
            Assert.Contains("Итоговая сумма: 25.50 MYR", reply);
            Assert.Contains("Категория: Транспорт → Такси", reply);
            Assert.Contains("Счёт: Наличные", reply);
            Assert.Contains("Дата: 27.08.2026 18:30 +08:00", reply);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ProcessDueAsync_ReceiptConfirmationShowsEveryItemAndVerifiedTotal()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-receipt-confirmation-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, categoryId) = await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"Expense","amount":104.98,"currency":"MYR","description":"alexis ampang","merchant":"alexis ampang","accountId":{{accountId}},"toAccountId":null,"receipt":{"subtotal":104.98,"tax":0,"serviceCharge":0,"discount":0,"rounding":0,"total":104.98,"items":[{"name":"Суп","quantity":1,"unitPrice":28,"baseAmount":28,"discount":0,"taxAllocated":0,"serviceChargeAllocated":0,"finalAmount":28,"categoryId":{{categoryId}},"confidence":1},{"name":"Милкшейк","quantity":1,"unitPrice":19.5,"baseAmount":19.5,"discount":0,"taxAllocated":0,"serviceChargeAllocated":0,"finalAmount":19.5,"categoryId":{{categoryId}},"confidence":1},{"name":"Peach Sencha","quantity":1,"unitPrice":19.5,"baseAmount":19.5,"discount":0,"taxAllocated":0,"serviceChargeAllocated":0,"finalAmount":19.5,"categoryId":{{categoryId}},"confidence":1},{"name":"Тирамису","quantity":1,"unitPrice":37.98,"baseAmount":37.98,"discount":0,"taxAllocated":0,"serviceChargeAllocated":0,"finalAmount":37.98,"categoryId":{{categoryId}},"confidence":1}]},"categoryId":{{categoryId}},"transactionDate":null,"clarificationQuestion":null}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            var reply = Assert.Single(telegram.SentMessages);
            Assert.Contains("Итоговая сумма: 104.98 MYR", reply);
            Assert.Contains("Позиции (4):", reply);
            Assert.Contains("1. Суп — 1 × 28.00 MYR; итог: 28.00 MYR", reply);
            Assert.Contains("2. Милкшейк — 1 × 19.50 MYR; итог: 19.50 MYR", reply);
            Assert.Contains("3. Peach Sencha — 1 × 19.50 MYR; итог: 19.50 MYR", reply);
            Assert.Contains("4. Тирамису — 1 × 37.98 MYR; итог: 37.98 MYR", reply);
            Assert.Contains("Проверка: сумма позиций 104.98 MYR совпадает с итогом ✅", reply);
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_CorrectsOwnedTransactionAndWritesAuditLog()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-correction-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, categoryId) = await SeedAsync(factory);
            long transactionId;
            await using (var db = factory.CreateDbContext())
            {
                var userId = (await db.Users.SingleAsync()).Id;
                var transaction = new Transaction { CreatedByUserId = userId, Type = TransactionType.Expense, TransactionDate = DateTimeOffset.UtcNow, CurrencyCode = "MYR", Amount = 34, Description = "Kiku Zakura", MerchantName = "Kiku Zakura", CategoryId = categoryId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Movements = [new AccountMovement { AccountId = accountId, Amount = -34 }] };
                db.Transactions.Add(transaction); await db.SaveChangesAsync(); transactionId = transaction.Id;
            }
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"Correction","amount":340,"currency":"MYR","description":"Kiku Zakura","merchant":"Kiku Zakura","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{categoryId}},"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null,"targetTransactionId":{{transactionId}}}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var resultDb = factory.CreateDbContext();
            var corrected = await resultDb.Transactions.Include(x => x.Movements).SingleAsync();
            Assert.Equal(340m, corrected.Amount); Assert.Equal(-340m, Assert.Single(corrected.Movements).Amount);
            var audit = await resultDb.AuditLog.SingleAsync(); Assert.Equal("Update", audit.Action); Assert.Contains("\"amount\":34", audit.OldJson, StringComparison.OrdinalIgnoreCase); Assert.Contains("\"amount\":340", audit.NewJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"ID {transactionId} исправлена", Assert.Single(telegram.SentMessages));
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_DeleteRequiresOneExactOwnedId()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-delete-guard-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath); await SeedAsync(factory);
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService("""
                {"kind":"Delete","amount":0,"currency":"MYR","description":"Все расходы","merchant":null,"accountId":null,"toAccountId":null,"receipt":null,"categoryId":null,"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null,"targetTransactionId":null}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var resultDb = factory.CreateDbContext();
            Assert.Empty(await resultDb.AuditLog.ToListAsync());
            Assert.Contains("точный ID одной операции", Assert.Single(telegram.SentMessages));
        }
        finally { File.Delete(databasePath); }
    }

    [Fact]
    public async Task ProcessDueAsync_ReclassifiesPayableDebtUsingStoredAmountAndDate()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-debt-expense-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            var (accountId, _) = await SeedAsync(factory);
            long debtId; long categoryId;
            var debtDate = new DateTimeOffset(2026, 8, 31, 15, 27, 0, TimeSpan.FromHours(8));
            await using (var db = factory.CreateDbContext())
            {
                var userId = (await db.Users.SingleAsync()).Id;
                categoryId = (await db.Categories.SingleAsync(x => x.Name == "Рестораны и кафе")).Id;
                var debt = new Debt { CreatedByUserId = userId, Direction = DebtDirection.Payable, Counterparty = "Максим", CurrencyCode = "MYR", OriginalAmount = 104.98m, Description = "Ужин в Alexis Ampang", CreatedAt = debtDate, Status = DebtStatus.Open };
                db.Debts.Add(debt); await db.SaveChangesAsync(); debtId = debt.Id;
            }
            var telegram = new RecordingTelegramClient();
            var extraction = new StubExtractionService($$"""
                {"kind":"DebtToExpense","amount":0,"currency":"","description":"","merchant":"Alexis Ampang","accountId":{{accountId}},"toAccountId":null,"receipt":null,"categoryId":{{categoryId}},"transactionDate":null,"clarificationQuestion":null,"debtDirection":null,"counterparty":null,"targetTransactionId":null,"targetDebtId":{{debtId}}}
                """);
            var processor = new BatchProcessor(factory, telegram, new StubTranscriptionService(), extraction, new StubAnalyticsService(), new StubQueryExecutor(), new FinanceOptions { MessageBatchDelaySeconds = 0 }, NullLogger<BatchProcessor>.Instance);

            await processor.ProcessDueAsync(CancellationToken.None);

            await using var resultDb = factory.CreateDbContext();
            var transaction = await resultDb.Transactions.Include(x => x.Movements).SingleAsync();
            Assert.Equal(104.98m, transaction.Amount); Assert.Equal("MYR", transaction.CurrencyCode); Assert.Equal(debtDate, transaction.TransactionDate);
            Assert.Equal(categoryId, transaction.CategoryId); Assert.Equal(-104.98m, Assert.Single(transaction.Movements).Amount);
            Assert.Equal(DebtStatus.Cancelled, (await resultDb.Debts.SingleAsync()).Status);
            Assert.Equal("ReclassifyToExpense", (await resultDb.AuditLog.SingleAsync()).Action);
            Assert.Contains($"Долг ID {debtId} записан как расход", Assert.Single(telegram.SentMessages));
        }
        finally { File.Delete(databasePath); }
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
        public Task SendRichMessageAsync(long chatId, string text, CancellationToken ct) { SentMessages.Add(text); return Task.CompletedTask; }
    }

    private sealed class StubExtractionService(string output) : IAiExtractionService
    {
        public Task<string> ExtractAsync(AiInput input, CancellationToken ct) => Task.FromResult(output);
    }

    private sealed class SequencedExtractionService(params string[] outputs) : IAiExtractionService
    {
        private int index;
        public List<AiInput> Inputs { get; } = [];
        public Task<string> ExtractAsync(AiInput input, CancellationToken ct)
        {
            Inputs.Add(input);
            return Task.FromResult(outputs[index++]);
        }
    }


    private sealed class StubAnalyticsService : IAiAnalyticsService
    {
        public Task<AnalyticsPlan> PlanAsync(AiInput input, CancellationToken ct) => Task.FromResult(new AnalyticsPlan(false, null, null));
        public Task<string> AnswerAsync(AiInput input, AnalyticsPlan plan, string queryResultsJson, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SequencedAnalyticsService(params AnalyticsPlan[] plans) : IAiAnalyticsService
    {
        private int index;
        public List<AiInput> PlanInputs { get; } = [];
        public Task<AnalyticsPlan> PlanAsync(AiInput input, CancellationToken ct) { PlanInputs.Add(input); return Task.FromResult(plans[index++]); }
        public Task<string> AnswerAsync(AiInput input, AnalyticsPlan plan, string queryResultsJson, CancellationToken ct) => Task.FromResult(queryResultsJson);
    }

    private sealed class StubQueryExecutor : IAnalyticsQueryExecutor
    {
        public Task<string> ExecuteAsync(string sql, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubTranscriptionService : IVoiceTranscriptionService
    {
        public Task<string> TranscribeAsync(TelegramFile file, CancellationToken ct) => throw new NotSupportedException();
    }
}
