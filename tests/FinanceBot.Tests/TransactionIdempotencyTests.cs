using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceBot.Tests;

public sealed class TransactionIdempotencyTests
{
    [Fact]
    public async Task DatabaseAllowsSeveralTransactionsForSameInputBatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-idempotency-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new FinanceDbContext(options);
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User { TelegramUserId = 42, Name = "Test", CreatedAt = now };
            var batch = new InputBatch { User = user, StartedAt = now, LastMessageAt = now, Status = BatchStatus.Completed };
            db.InputBatches.Add(batch);
            await db.SaveChangesAsync();
            db.Transactions.Add(CreateTransaction(user.Id, batch.Id, now, 0));
            await db.SaveChangesAsync();

            db.Transactions.Add(CreateTransaction(user.Id, batch.Id, now, 1));

            await db.SaveChangesAsync();

            Assert.Equal(2, await db.Transactions.CountAsync(x => x.InputBatchId == batch.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateOperationIndexWithinBatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-operation-idempotency-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new FinanceDbContext(options);
            await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new User { TelegramUserId = 42, Name = "Test", CreatedAt = now };
            var batch = new InputBatch { User = user, StartedAt = now, LastMessageAt = now, Status = BatchStatus.Completed };
            db.InputBatches.Add(batch);
            await db.SaveChangesAsync();
            db.Transactions.Add(CreateTransaction(user.Id, batch.Id, now, 0));
            await db.SaveChangesAsync();
            db.Transactions.Add(CreateTransaction(user.Id, batch.Id, now, 0));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { File.Delete(databasePath); }
    }

    private static Transaction CreateTransaction(long userId, long batchId, DateTimeOffset now, int operationIndex) => new()
    {
        CreatedByUserId = userId,
        Type = TransactionType.Expense,
        TransactionDate = now,
        CurrencyCode = "MYR",
        Amount = 10,
        Description = "Test",
        InputBatchId = batchId,
        InputBatchOperationIndex = operationIndex,
        CreatedAt = now,
        UpdatedAt = now
    };
}
