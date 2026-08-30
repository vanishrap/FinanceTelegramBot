using FinanceBot.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Persistence;
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>(); public DbSet<Account> Accounts => Set<Account>(); public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>(); public DbSet<AccountMovement> AccountMovements => Set<AccountMovement>(); public DbSet<ExchangeDetail> ExchangeDetails => Set<ExchangeDetail>();
    public DbSet<Receipt> Receipts => Set<Receipt>(); public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>(); public DbSet<Debt> Debts => Set<Debt>(); public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<InputBatch> InputBatches => Set<InputBatch>(); public DbSet<InputMessage> InputMessages => Set<InputMessage>(); public DbSet<AiRun> AiRuns => Set<AiRun>(); public DbSet<ValidationResult> ValidationResults => Set<ValidationResult>(); public DbSet<AuditLog> AuditLog => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.TelegramUserId).IsUnique();
        b.Entity<Account>().HasIndex(x => new { x.Name, x.CurrencyCode, x.OwnerUserId }).IsUnique();
        b.Entity<Category>().HasIndex(x => new { x.ParentId, x.Name, x.Type }).IsUnique();
        b.Entity<InputMessage>().HasIndex(x => x.TelegramMessageId).IsUnique();
        b.Entity<InputBatch>().HasIndex(x => new { x.Status, x.LastMessageAt });
        b.Entity<Transaction>().HasIndex(x => x.InputBatchId).IsUnique();
        b.Entity<AccountMovement>().HasIndex(x => new { x.AccountId, x.TransactionId });
        b.Entity<Receipt>().HasIndex(x => x.TransactionId).IsUnique();
        b.Entity<ExchangeDetail>().HasKey(x => x.TransactionId);
        b.Entity<ExchangeDetail>().HasOne(x => x.Transaction).WithOne().HasForeignKey<ExchangeDetail>(x => x.TransactionId);
        b.Entity<DebtPayment>().HasIndex(x => new { x.DebtId, x.PaidAt });
        b.Entity<Debt>().HasIndex(x => x.InputBatchId).IsUnique();
        b.Entity<AiRun>().HasIndex(x => x.InputBatchId);
        b.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
        foreach (var type in b.Model.GetEntityTypes()) foreach (var property in type.GetProperties().Where(p => p.ClrType == typeof(decimal))) { property.SetPrecision(18); property.SetScale(4); }
        b.Entity<Transaction>().Property(x => x.Type).HasConversion<string>(); b.Entity<Account>().Property(x => x.Type).HasConversion<string>(); b.Entity<Category>().Property(x => x.Type).HasConversion<string>();
        b.Entity<InputBatch>().Property(x => x.Status).HasConversion<string>(); b.Entity<InputMessage>().Property(x => x.Type).HasConversion<string>(); b.Entity<Debt>().Property(x => x.Direction).HasConversion<string>(); b.Entity<Debt>().Property(x => x.Status).HasConversion<string>();
        b.Entity<AiRun>().Property(x => x.Status).HasConversion<string>(); b.Entity<ValidationResult>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Account>().HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Category>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transaction>().HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DebtPayment>().HasOne(x => x.Transaction).WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
