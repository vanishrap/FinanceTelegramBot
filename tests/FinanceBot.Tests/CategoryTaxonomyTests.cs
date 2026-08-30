using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceBot.Tests;

public sealed class CategoryTaxonomyTests
{
    [Fact]
    public async Task Migrations_SeedHierarchicalExpenseAndIncomeCategories()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"finance-taxonomy-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new FinanceDbContext(options);

            await db.Database.MigrateAsync();

            var taxi = await db.Categories.Include(x => x.Parent).SingleAsync(x => x.Name == "Такси");
            Assert.Equal("Транспорт", taxi.Parent?.Name);
            Assert.Equal(CategoryType.Expense, taxi.Type);
            Assert.Contains(await db.Categories.ToListAsync(), x => x.Name == "Зарплата" && x.Type == CategoryType.Income);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
