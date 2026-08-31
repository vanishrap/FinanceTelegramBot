using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608310006_AddDetailedShoppingCategories")]
public sealed class AddDetailedShoppingCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Косметика', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Личная гигиена', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Бытовая химия', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Канцелярия', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Аксессуары', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Лекарства', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Медицинские товары', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE FROM Categories WHERE Name IN
('Косметика','Личная гигиена','Бытовая химия','Канцелярия','Аксессуары','Лекарства','Медицинские товары')
AND Type='Expense' AND ParentId IS NOT NULL;
""");
    }
}
