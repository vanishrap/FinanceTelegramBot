using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceBot.Tests;

public sealed class AnalyticsQueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSharedFamilyRows()
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
                    """, CancellationToken.None);
                Assert.Contains("Coffee",json); Assert.Contains("Private",json);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExecuteAsync_CanQuerySharedReceiptItems()
    {
        var path=Path.Combine(Path.GetTempPath(), $"finance-receipt-analytics-{Guid.NewGuid():N}.db");
        try
        {
            var factory=new Factory(path);
            await using(var db=await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
                var first=new User{TelegramUserId=1,Name="First",CreatedAt=DateTimeOffset.UtcNow};
                var second=new User{TelegramUserId=2,Name="Second",CreatedAt=DateTimeOffset.UtcNow};
                db.Receipts.AddRange(
                    ReceiptFor(first,"Watsons","Razor",29.92m),
                    ReceiptFor(second,"Private shop","Secret item",999m));
                await db.SaveChangesAsync();
                var json=await new AnalyticsQueryExecutor(factory).ExecuteAsync("""
                    SELECT r.Id AS ReceiptId, r.MerchantName, ri.NormalizedName, ri.FinalAmount
                    FROM ReceiptItems ri
                    JOIN Receipts r ON r.Id=ri.ReceiptId
                    ORDER BY r.Id DESC, ri.Id;
                    """,CancellationToken.None);
                Assert.Contains("Watsons",json);Assert.Contains("Razor",json);Assert.Contains("29.92",json);
                Assert.Contains("Secret item",json);Assert.Contains("999",json);
            }
        }
        finally { File.Delete(path); }

        static Receipt ReceiptFor(User user,string merchant,string item,decimal amount) => new()
        {
            Transaction=new Transaction{CreatedByUser=user,Type=TransactionType.Expense,TransactionDate=DateTimeOffset.UtcNow,CurrencyCode="MYR",Amount=amount,Description=merchant,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow},
            MerchantName=merchant,ReceiptDate=DateTimeOffset.UtcNow,Subtotal=amount,Total=amount,CurrencyCode="MYR",
            Items=[new ReceiptItem{RawName=item,NormalizedName=item,Quantity=1,UnitPrice=amount,BaseAmount=amount,FinalAmount=amount,AiConfidence=1}]
        };
    }

    [Theory]
    [InlineData("DELETE FROM Transactions")]
    [InlineData("SELECT * FROM Transactions; DROP TABLE Transactions")]
    public async Task ExecuteAsync_RejectsUnsafeSql(string sql)
    {
        var factory=new Factory(Path.Combine(Path.GetTempPath(), $"finance-analytics-{Guid.NewGuid():N}.db"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new AnalyticsQueryExecutor(factory).ExecuteAsync(sql,CancellationToken.None));
    }

    private sealed class Factory(string path) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> options=new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={path}").Options;
        public FinanceDbContext CreateDbContext()=>new(options);
    }
}
