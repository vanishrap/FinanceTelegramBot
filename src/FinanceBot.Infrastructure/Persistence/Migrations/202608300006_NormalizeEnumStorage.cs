using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608300006_NormalizeEnumStorage")]
public sealed class NormalizeEnumStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
UPDATE Transactions SET Type = CASE lower(trim(Type))
  WHEN '0' THEN 'Expense' WHEN 'расход' THEN 'Expense' WHEN 'expense' THEN 'Expense'
  WHEN '1' THEN 'Income' WHEN 'доход' THEN 'Income' WHEN 'income' THEN 'Income'
  WHEN '2' THEN 'Transfer' WHEN 'перевод' THEN 'Transfer' WHEN 'transfer' THEN 'Transfer'
  WHEN '3' THEN 'BalanceAdjustment' WHEN 'корректировка баланса' THEN 'BalanceAdjustment' WHEN 'balanceadjustment' THEN 'BalanceAdjustment'
  WHEN '4' THEN 'DebtSettlement' WHEN 'погашение долга' THEN 'DebtSettlement' WHEN 'debtsettlement' THEN 'DebtSettlement'
  ELSE Type END;
UPDATE Categories SET Type = CASE lower(trim(Type))
  WHEN '0' THEN 'Expense' WHEN 'расход' THEN 'Expense' WHEN 'expense' THEN 'Expense'
  WHEN '1' THEN 'Income' WHEN 'доход' THEN 'Income' WHEN 'income' THEN 'Income'
  ELSE Type END;
UPDATE Debts SET Direction = CASE lower(trim(Direction))
  WHEN '0' THEN 'Payable' WHEN 'я должен' THEN 'Payable' WHEN 'payable' THEN 'Payable'
  WHEN '1' THEN 'Receivable' WHEN 'мне должны' THEN 'Receivable' WHEN 'receivable' THEN 'Receivable'
  ELSE Direction END;
""");

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
