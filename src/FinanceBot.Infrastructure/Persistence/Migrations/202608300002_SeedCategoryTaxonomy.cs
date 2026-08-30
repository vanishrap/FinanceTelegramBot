using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608300002_SeedCategoryTaxonomy")]
public sealed class SeedCategoryTaxonomy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Транспорт', NULL, 'Expense', 1),
('Еда', NULL, 'Expense', 1),
('Здоровье', NULL, 'Expense', 1),
('Жильё', NULL, 'Expense', 1),
('Покупки', NULL, 'Expense', 1),
('Развлечения', NULL, 'Expense', 1),
('Образование', NULL, 'Expense', 1),
('Связь', NULL, 'Expense', 1),
('Путешествия', NULL, 'Expense', 1),
('Семья', NULL, 'Expense', 1),
('Финансовые расходы', NULL, 'Expense', 1),
('Прочие расходы', NULL, 'Expense', 1),
('Зарплата', NULL, 'Income', 1),
('Фриланс', NULL, 'Income', 1),
('Подарки', NULL, 'Income', 1),
('Инвестиционный доход', NULL, 'Income', 1),
('Возвраты', NULL, 'Income', 1),
('Прочие доходы', NULL, 'Income', 1);

INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Такси', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Общественный транспорт', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Топливо', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Парковка', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Ремонт и обслуживание транспорта', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Продукты', (SELECT Id FROM Categories WHERE Name='Еда' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Рестораны и кафе', (SELECT Id FROM Categories WHERE Name='Еда' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Доставка еды', (SELECT Id FROM Categories WHERE Name='Еда' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Фастфуд', (SELECT Id FROM Categories WHERE Name='Еда' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Аптеки', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Врачи', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Анализы', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Стоматология', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Спорт и фитнес', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Аренда и ипотека', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Коммунальные услуги', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Ремонт жилья', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Мебель и товары для дома', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Одежда и обувь', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Электроника', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Подписки', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Кино и мероприятия', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Курсы и книги', (SELECT Id FROM Categories WHERE Name='Образование' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Мобильная связь', (SELECT Id FROM Categories WHERE Name='Связь' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Интернет', (SELECT Id FROM Categories WHERE Name='Связь' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Отели', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Билеты', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Дети', (SELECT Id FROM Categories WHERE Name='Семья' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Питомцы', (SELECT Id FROM Categories WHERE Name='Семья' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Комиссии', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Налоги', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE FROM Categories WHERE ParentId IN (
    SELECT Id FROM Categories WHERE ParentId IS NULL AND Name IN
    ('Транспорт','Еда','Здоровье','Жильё','Покупки','Развлечения','Образование','Связь','Путешествия','Семья','Финансовые расходы','Прочие расходы')
);
DELETE FROM Categories WHERE ParentId IS NULL AND Name IN
('Транспорт','Еда','Здоровье','Жильё','Покупки','Развлечения','Образование','Связь','Путешествия','Семья','Финансовые расходы','Прочие расходы','Зарплата','Фриланс','Подарки','Инвестиционный доход','Возвраты','Прочие доходы');
""");
    }
}
