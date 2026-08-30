using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("202608300003_PreventDuplicateBatchTransactions")]
public sealed class PreventDuplicateBatchTransactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the oldest transaction for batches that were processed concurrently.
        // Related movements and receipts are removed by their cascading foreign keys.
        migrationBuilder.Sql("""
DELETE FROM Transactions
WHERE InputBatchId IS NOT NULL
  AND Id NOT IN (SELECT MIN(Id) FROM Transactions WHERE InputBatchId IS NOT NULL GROUP BY InputBatchId);
DROP INDEX IX_Transactions_InputBatchId;
CREATE UNIQUE INDEX IX_Transactions_InputBatchId ON Transactions(InputBatchId);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP INDEX IX_Transactions_InputBatchId;
CREATE INDEX IX_Transactions_InputBatchId ON Transactions(InputBatchId);
""");
    }
}
