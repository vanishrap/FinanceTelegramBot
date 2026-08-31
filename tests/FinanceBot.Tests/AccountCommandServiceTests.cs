using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceBot.Tests;

public sealed class AccountCommandServiceTests
{
    [Fact]
    public async Task AccountAndAccountsCommands_CreateAndListSharedAccount()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-accounts-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();
            var service = new AccountCommandService(factory, new FinanceOptions { AllowedTelegramUserIds = [42, 43] });

            var created = await service.TryHandleAsync(new TelegramUpdate(1, 10, 42, "Test", "/account Основная карта MYR bank", null, null), CancellationToken.None);
            var listed = await service.TryHandleAsync(new TelegramUpdate(2, 11, 43, "Family", "/accounts", null, null), CancellationToken.None);

            Assert.True(created.Handled);
            Assert.Contains("Счёт создан", created.Response);
            Assert.Contains("Основная карта", listed.Response);
            Assert.Contains("0.00 MYR", listed.Response);
            await using var verification = factory.CreateDbContext();
            var account = await verification.Accounts.SingleAsync(x => x.Name == "Основная карта");
            Assert.Equal(AccountType.Bank, account.Type);
            Assert.Null(account.OwnerUserId);
            Assert.Contains("Общие счета", listed.Response);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AccountCommand_IsRejectedForUnauthorizedUser()
    {
        var service = new AccountCommandService(new ThrowingDbContextFactory(), new FinanceOptions());

        var result = await service.TryHandleAsync(new TelegramUpdate(1, 10, 99, "Other", "/account Cash MYR cash", null, null), CancellationToken.None);

        Assert.True(result.Handled);
        Assert.Contains("нет доступа", result.Response);
    }

    private sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        public FinanceDbContext CreateDbContext() => new(options);
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<FinanceDbContext>
    {
        public FinanceDbContext CreateDbContext() => throw new InvalidOperationException("Database must not be used for unauthorized commands.");
    }
}
