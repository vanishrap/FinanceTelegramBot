using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceBot.Tests;

public sealed class AnalyticsQueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyRequestedUsersRows()
    {
        var path=Path.Combine(Path.GetTempPath(), $"finance-analytics-{Guid.NewGuid():N}.db");
        try
        {
            var factory=new Factory(path);
            await using(var db=await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
                var first=new User{TelegramUserId=1,Name="First",CreatedAt=DateTimeOffset.UtcNow};
                var second=new User{TelegramUserId=2,Name="Second",CreatedAt=DateTimeOffset.UtcNow};
                db.Transactions.AddRange(
                    new Transaction{CreatedByUser=first,Type=TransactionType.Expense,TransactionDate=DateTimeOffset.UtcNow,CurrencyCode="MYR",Amount=12,Description="Coffee"},
                    new Transaction{CreatedByUser=second,Type=TransactionType.Expense,TransactionDate=DateTimeOffset.UtcNow,CurrencyCode="MYR",Amount=99,Description="Private"});
                await db.SaveChangesAsync();
                var json=await new AnalyticsQueryExecutor(factory).ExecuteAsync("""
                    SELECT Description, Amount
                    FROM Transactions
                    WHERE CreatedByUserId = $userId;
                    """, first.Id, CancellationToken.None);
                Assert.Contains("Coffee",json); Assert.DoesNotContain("Private",json);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("DELETE FROM Transactions WHERE CreatedByUserId=$userId")]
    [InlineData("SELECT * FROM Transactions WHERE CreatedByUserId=$userId; DROP TABLE Transactions")]
    [InlineData("SELECT * FROM Transactions")]
    public async Task ExecuteAsync_RejectsUnsafeSql(string sql)
    {
        var factory=new Factory(Path.Combine(Path.GetTempPath(), $"finance-analytics-{Guid.NewGuid():N}.db"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new AnalyticsQueryExecutor(factory).ExecuteAsync(sql,1,CancellationToken.None));
    }

    private sealed class Factory(string path) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> options=new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={path}").Options;
        public FinanceDbContext CreateDbContext()=>new(options);
    }
}
