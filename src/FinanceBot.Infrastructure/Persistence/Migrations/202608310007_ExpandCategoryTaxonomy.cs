using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608310007_ExpandCategoryTaxonomy")]
public sealed class ExpandCategoryTaxonomy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Уход за собой', NULL, 'Expense', 1);

INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Каршеринг и аренда авто', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Платные дороги', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Страхование транспорта', (SELECT Id FROM Categories WHERE Name='Транспорт' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Кофейни', (SELECT Id FROM Categories WHERE Name='Еда' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Психотерапия', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Оптика', (SELECT Id FROM Categories WHERE Name='Здоровье' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Уборка', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Страхование жилья', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Сад и инструменты', (SELECT Id FROM Categories WHERE Name='Жильё' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Украшения и часы', (SELECT Id FROM Categories WHERE Name='Покупки' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Парикмахерская', (SELECT Id FROM Categories WHERE Name='Уход за собой' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Маникюр и педикюр', (SELECT Id FROM Categories WHERE Name='Уход за собой' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Косметология', (SELECT Id FROM Categories WHERE Name='Уход за собой' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Массаж и спа', (SELECT Id FROM Categories WHERE Name='Уход за собой' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Игры', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Хобби', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Музыка', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Ночная жизнь', (SELECT Id FROM Categories WHERE Name='Развлечения' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Обучение', (SELECT Id FROM Categories WHERE Name='Образование' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Книги', (SELECT Id FROM Categories WHERE Name='Образование' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Языковые курсы', (SELECT Id FROM Categories WHERE Name='Образование' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Учебные материалы', (SELECT Id FROM Categories WHERE Name='Образование' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Почта и доставка', (SELECT Id FROM Categories WHERE Name='Связь' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Облачные сервисы', (SELECT Id FROM Categories WHERE Name='Связь' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Визы и документы', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Страхование путешествий', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Экскурсии', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Транспорт в поездках', (SELECT Id FROM Categories WHERE Name='Путешествия' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Поддержка родителей', (SELECT Id FROM Categories WHERE Name='Семья' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Уход и няня', (SELECT Id FROM Categories WHERE Name='Семья' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Проценты по кредитам', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Обмен валют', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Штрафы и пени', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Страхование', (SELECT Id FROM Categories WHERE Name='Финансовые расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Благотворительность', (SELECT Id FROM Categories WHERE Name='Прочие расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Подарки другим', (SELECT Id FROM Categories WHERE Name='Прочие расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Документы и госпошлины', (SELECT Id FROM Categories WHERE Name='Прочие расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1),
('Профессиональные услуги', (SELECT Id FROM Categories WHERE Name='Прочие расходы' AND Type='Expense' AND ParentId IS NULL), 'Expense', 1);

INSERT INTO Categories (Name, ParentId, Type, IsActive) VALUES
('Бонусы', NULL, 'Income', 1),
('Проценты', NULL, 'Income', 1),
('Дивиденды', NULL, 'Income', 1),
('Арендный доход', NULL, 'Income', 1),
('Продажа имущества', NULL, 'Income', 1),
('Социальные выплаты', NULL, 'Income', 1),
('Страховые выплаты', NULL, 'Income', 1);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE FROM Categories WHERE Type='Expense' AND ParentId IS NOT NULL AND Name IN
('Каршеринг и аренда авто','Платные дороги','Страхование транспорта','Кофейни','Психотерапия','Оптика','Уборка','Страхование жилья','Сад и инструменты','Украшения и часы','Парикмахерская','Маникюр и педикюр','Косметология','Массаж и спа','Игры','Хобби','Музыка','Ночная жизнь','Обучение','Книги','Языковые курсы','Учебные материалы','Почта и доставка','Облачные сервисы','Визы и документы','Страхование путешествий','Экскурсии','Транспорт в поездках','Поддержка родителей','Уход и няня','Проценты по кредитам','Обмен валют','Штрафы и пени','Страхование','Благотворительность','Подарки другим','Документы и госпошлины','Профессиональные услуги');
DELETE FROM Categories WHERE Name='Уход за собой' AND Type='Expense' AND ParentId IS NULL;
DELETE FROM Categories WHERE Type='Income' AND ParentId IS NULL AND Name IN
('Бонусы','Проценты','Дивиденды','Арендный доход','Продажа имущества','Социальные выплаты','Страховые выплаты');
""");
    }
}
