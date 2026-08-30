using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceBot.Tests;

public sealed class MessageBatchServiceTests
{
    [Fact]
    public async Task AddAsync_AppendsToMostRecentCollectingBatch_WithSqlite()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-bot-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            await SeedBatchesAsync(factory);
            var service = new MessageBatchService(factory, new FinanceOptions
            {
                AllowedTelegramUserIds = [42]
            }, NullLogger<MessageBatchService>.Instance);

            var accepted = await service.AddAsync(new TelegramUpdate(1, 101, 42, "Test", "Coffee", null, null), CancellationToken.None);

            Assert.True(accepted);
            await using var db = factory.CreateDbContext();
            var batches = await db.InputBatches.Include(x => x.Messages).OrderBy(x => x.Id).ToListAsync();
            Assert.Empty(batches[0].Messages);
            Assert.Single(batches[1].Messages);
            Assert.Equal("Coffee", batches[1].Messages[0].Text);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task SeedBatchesAsync(IDbContextFactory<FinanceDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        var user = new User { TelegramUserId = 42, Name = "Test", CreatedAt = DateTimeOffset.UtcNow };
        db.InputBatches.AddRange(
            new InputBatch { User = user, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10), LastMessageAt = DateTimeOffset.UtcNow.AddMinutes(-10), Status = BatchStatus.Collecting },
            new InputBatch { User = user, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5), LastMessageAt = DateTimeOffset.UtcNow.AddMinutes(-5), Status = BatchStatus.Collecting });
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        public FinanceDbContext CreateDbContext() => new(options);
    }
}
