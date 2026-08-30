using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608300004_AddDebtInputBatch")]
public sealed class AddDebtInputBatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "InputBatchId", table: "Debts", type: "INTEGER", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Debts_InputBatchId", table: "Debts", column: "InputBatchId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Debts_InputBatchId", table: "Debts");
        migrationBuilder.DropColumn(name: "InputBatchId", table: "Debts");
    }
}
