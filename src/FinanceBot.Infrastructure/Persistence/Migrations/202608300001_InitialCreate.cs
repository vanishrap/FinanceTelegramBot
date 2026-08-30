using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FinanceBot.Infrastructure.Persistence;

#nullable disable
namespace FinanceBot.Infrastructure.Persistence.Migrations;
[DbContext(typeof(FinanceDbContext))]
[Migration("202608300001_InitialCreate")]
public sealed class InitialCreate : Migration
{
 protected override void Up(MigrationBuilder m)
 {
  m.Sql("""
PRAGMA foreign_keys=ON;
CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, TelegramUserId INTEGER NOT NULL, Name TEXT NOT NULL, CreatedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IX_Users_TelegramUserId ON Users(TelegramUserId);
CREATE TABLE Accounts (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, CurrencyCode TEXT NOT NULL, Type TEXT NOT NULL, OwnerUserId INTEGER NULL REFERENCES Users(Id) ON DELETE RESTRICT, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IX_Accounts_Name_CurrencyCode_OwnerUserId ON Accounts(Name,CurrencyCode,OwnerUserId);
CREATE TABLE Categories (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ParentId INTEGER NULL REFERENCES Categories(Id) ON DELETE RESTRICT, Type TEXT NOT NULL, IsActive INTEGER NOT NULL);
CREATE UNIQUE INDEX IX_Categories_ParentId_Name_Type ON Categories(ParentId,Name,Type);
CREATE TABLE InputBatches (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE, StartedAt TEXT NOT NULL, LastMessageAt TEXT NOT NULL, ProcessingStartedAt TEXT NULL, CompletedAt TEXT NULL, Status TEXT NOT NULL);
CREATE INDEX IX_InputBatches_Status_LastMessageAt ON InputBatches(Status,LastMessageAt);
CREATE TABLE InputMessages (Id INTEGER PRIMARY KEY AUTOINCREMENT, InputBatchId INTEGER NOT NULL REFERENCES InputBatches(Id) ON DELETE CASCADE, TelegramMessageId INTEGER NOT NULL, Type TEXT NOT NULL, Text TEXT NULL, Transcript TEXT NULL, TelegramFileId TEXT NULL, CreatedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IX_InputMessages_TelegramMessageId ON InputMessages(TelegramMessageId);
CREATE TABLE Transactions (Id INTEGER PRIMARY KEY AUTOINCREMENT, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT, Type TEXT NOT NULL, TransactionDate TEXT NOT NULL, CurrencyCode TEXT NOT NULL, Amount TEXT NOT NULL, Description TEXT NOT NULL, MerchantName TEXT NULL, CategoryId INTEGER NULL REFERENCES Categories(Id), InputBatchId INTEGER NULL REFERENCES InputBatches(Id), CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
CREATE INDEX IX_Transactions_InputBatchId ON Transactions(InputBatchId);
CREATE TABLE AccountMovements (Id INTEGER PRIMARY KEY AUTOINCREMENT, TransactionId INTEGER NOT NULL REFERENCES Transactions(Id) ON DELETE CASCADE, AccountId INTEGER NOT NULL REFERENCES Accounts(Id) ON DELETE CASCADE, Amount TEXT NOT NULL);
CREATE INDEX IX_AccountMovements_AccountId_TransactionId ON AccountMovements(AccountId,TransactionId);
CREATE TABLE ExchangeDetails (TransactionId INTEGER PRIMARY KEY REFERENCES Transactions(Id) ON DELETE CASCADE, FromAmount TEXT NOT NULL, FromCurrency TEXT NOT NULL, ToAmount TEXT NOT NULL, ToCurrency TEXT NOT NULL, ExchangeRate TEXT NOT NULL);
CREATE TABLE Receipts (Id INTEGER PRIMARY KEY AUTOINCREMENT, TransactionId INTEGER NOT NULL REFERENCES Transactions(Id) ON DELETE CASCADE, MerchantName TEXT NOT NULL, ReceiptDate TEXT NULL, Subtotal TEXT NOT NULL, Tax TEXT NOT NULL, ServiceCharge TEXT NOT NULL, Discount TEXT NOT NULL, Rounding TEXT NOT NULL, Total TEXT NOT NULL, CurrencyCode TEXT NOT NULL, RawExtractedJson TEXT NOT NULL);
CREATE UNIQUE INDEX IX_Receipts_TransactionId ON Receipts(TransactionId);
CREATE TABLE ReceiptItems (Id INTEGER PRIMARY KEY AUTOINCREMENT, ReceiptId INTEGER NOT NULL REFERENCES Receipts(Id) ON DELETE CASCADE, RawName TEXT NOT NULL, NormalizedName TEXT NOT NULL, Quantity TEXT NOT NULL, UnitPrice TEXT NOT NULL, BaseAmount TEXT NOT NULL, Discount TEXT NOT NULL, TaxAllocated TEXT NOT NULL, ServiceChargeAllocated TEXT NOT NULL, FinalAmount TEXT NOT NULL, CategoryId INTEGER NULL REFERENCES Categories(Id), AiConfidence TEXT NOT NULL, WasUserCorrected INTEGER NOT NULL);
CREATE TABLE Debts (Id INTEGER PRIMARY KEY AUTOINCREMENT, CreatedByUserId INTEGER NOT NULL REFERENCES Users(Id), Direction TEXT NOT NULL, Counterparty TEXT NOT NULL, CurrencyCode TEXT NOT NULL, OriginalAmount TEXT NOT NULL, Description TEXT NOT NULL, CreatedAt TEXT NOT NULL, Status TEXT NOT NULL);
CREATE TABLE DebtPayments (Id INTEGER PRIMARY KEY AUTOINCREMENT, DebtId INTEGER NOT NULL REFERENCES Debts(Id) ON DELETE CASCADE, TransactionId INTEGER NOT NULL REFERENCES Transactions(Id) ON DELETE RESTRICT, Amount TEXT NOT NULL, PaidAt TEXT NOT NULL);
CREATE INDEX IX_DebtPayments_DebtId_PaidAt ON DebtPayments(DebtId,PaidAt);
CREATE TABLE AiRuns (Id INTEGER PRIMARY KEY AUTOINCREMENT, InputBatchId INTEGER NOT NULL REFERENCES InputBatches(Id) ON DELETE CASCADE, Model TEXT NOT NULL, PromptVersion TEXT NOT NULL, InputJson TEXT NOT NULL, OutputJson TEXT NULL, StartedAt TEXT NOT NULL, CompletedAt TEXT NULL, Status TEXT NOT NULL, Error TEXT NULL);
CREATE INDEX IX_AiRuns_InputBatchId ON AiRuns(InputBatchId);
CREATE TABLE ValidationResults (Id INTEGER PRIMARY KEY AUTOINCREMENT, AiRunId INTEGER NOT NULL REFERENCES AiRuns(Id) ON DELETE CASCADE, Type TEXT NOT NULL, ExpectedValue TEXT NULL, ActualValue TEXT NULL, Difference TEXT NULL, Status TEXT NOT NULL, Message TEXT NOT NULL);
CREATE TABLE AuditLog (Id INTEGER PRIMARY KEY AUTOINCREMENT, EntityType TEXT NOT NULL, EntityId INTEGER NOT NULL, Action TEXT NOT NULL, OldJson TEXT NULL, NewJson TEXT NULL, UserId INTEGER NOT NULL, CreatedAt TEXT NOT NULL);
CREATE INDEX IX_AuditLog_EntityType_EntityId ON AuditLog(EntityType,EntityId);
""");
 }
 protected override void Down(MigrationBuilder m) { foreach (var table in new[]{"AuditLog","ValidationResults","AiRuns","DebtPayments","Debts","ReceiptItems","Receipts","ExchangeDetails","AccountMovements","Transactions","InputMessages","InputBatches","Categories","Accounts","Users"}) m.DropTable(table); }
}
