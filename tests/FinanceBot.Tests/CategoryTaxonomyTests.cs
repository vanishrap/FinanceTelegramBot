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
            var cosmetics = await db.Categories.Include(x => x.Parent).SingleAsync(x => x.Name == "Косметика");
            Assert.Equal("Покупки", cosmetics.Parent?.Name);
            var medicine = await db.Categories.Include(x => x.Parent).SingleAsync(x => x.Name == "Лекарства");
            Assert.Equal("Здоровье", medicine.Parent?.Name);
            var expenseRoots = await db.Categories.Where(x => x.Type == CategoryType.Expense && x.ParentId == null).ToListAsync();
            var parentIds = await db.Categories.Where(x => x.Type == CategoryType.Expense && x.ParentId != null).Select(x => x.ParentId!.Value).Distinct().ToListAsync();
            Assert.All(expenseRoots, root => Assert.Contains(root.Id, parentIds));
            var requiredCategories = new[] { "Каршеринг и аренда авто", "Кофейни", "Психотерапия", "Уборка", "Парикмахерская", "Игры", "Книги", "Облачные сервисы", "Визы и документы", "Поддержка родителей", "Проценты по кредитам", "Благотворительность", "Дивиденды", "Арендный доход" };
            var categoryNames = await db.Categories.Select(x => x.Name).ToListAsync();
            Assert.All(requiredCategories, name => Assert.Contains(name, categoryNames));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
