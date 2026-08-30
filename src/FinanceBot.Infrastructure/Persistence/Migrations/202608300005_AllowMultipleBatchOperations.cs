using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608300005_AllowMultipleBatchOperations")]
public sealed class AllowMultipleBatchOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Transactions_InputBatchId", table: "Transactions");
        migrationBuilder.AddColumn<int>(name: "InputBatchOperationIndex", table: "Transactions", type: "INTEGER", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Transactions_InputBatchId_InputBatchOperationIndex", table: "Transactions", columns: ["InputBatchId", "InputBatchOperationIndex"], unique: true);
        migrationBuilder.DropIndex(name: "IX_Debts_InputBatchId", table: "Debts");
        migrationBuilder.AddColumn<int>(name: "InputBatchOperationIndex", table: "Debts", type: "INTEGER", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Debts_InputBatchId_InputBatchOperationIndex", table: "Debts", columns: ["InputBatchId", "InputBatchOperationIndex"], unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Transactions_InputBatchId_InputBatchOperationIndex", table: "Transactions");
        migrationBuilder.DropColumn(name: "InputBatchOperationIndex", table: "Transactions");
        migrationBuilder.CreateIndex(name: "IX_Transactions_InputBatchId", table: "Transactions", column: "InputBatchId", unique: true);
        migrationBuilder.DropIndex(name: "IX_Debts_InputBatchId_InputBatchOperationIndex", table: "Debts");
        migrationBuilder.DropColumn(name: "InputBatchOperationIndex", table: "Debts");
        migrationBuilder.CreateIndex(name: "IX_Debts_InputBatchId", table: "Debts", column: "InputBatchId", unique: true);
    }
}
