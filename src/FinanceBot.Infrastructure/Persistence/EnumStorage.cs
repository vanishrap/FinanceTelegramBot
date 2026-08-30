using FinanceBot.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanceBot.Infrastructure.Persistence;

/// <summary>Canonical values written to SQLite. These values are also exposed to the analytics planner.</summary>
public static class EnumStorage
{
    public const string Expense = "Expense";
    public const string Income = "Income";
    public const string Transfer = "Transfer";
    public const string BalanceAdjustment = "BalanceAdjustment";
    public const string DebtSettlement = "DebtSettlement";
    public const string Payable = "Payable";
    public const string Receivable = "Receivable";

    public static readonly ValueConverter<TransactionType, string> TransactionTypeConverter = new(
        value => FormatTransactionType(value), value => ParseTransactionType(value));

    public static readonly ValueConverter<CategoryType, string> CategoryTypeConverter = new(
        value => FormatCategoryType(value), value => ParseCategoryType(value));

    public static readonly ValueConverter<DebtDirection, string> DebtDirectionConverter = new(
        value => FormatDebtDirection(value), value => ParseDebtDirection(value));

    public const string AnalyticsDescription = "SQLite enum columns use these exact case-sensitive TEXT values: Transactions.Type = Expense|Income|Transfer|BalanceAdjustment|DebtSettlement; Categories.Type = Expense|Income; Debts.Direction = Payable|Receivable. Never use numeric or translated enum values.";

    private static string FormatTransactionType(TransactionType value) => value switch { TransactionType.Expense => Expense, TransactionType.Income => Income, TransactionType.Transfer => Transfer, TransactionType.BalanceAdjustment => BalanceAdjustment, TransactionType.DebtSettlement => DebtSettlement, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static TransactionType ParseTransactionType(string value) => value switch { Expense => TransactionType.Expense, Income => TransactionType.Income, Transfer => TransactionType.Transfer, BalanceAdjustment => TransactionType.BalanceAdjustment, DebtSettlement => TransactionType.DebtSettlement, _ => throw new InvalidOperationException("Unknown stored transaction type: " + value) };
    private static string FormatCategoryType(CategoryType value) => value switch { CategoryType.Expense => Expense, CategoryType.Income => Income, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static CategoryType ParseCategoryType(string value) => value switch { Expense => CategoryType.Expense, Income => CategoryType.Income, _ => throw new InvalidOperationException("Unknown stored category type: " + value) };
    private static string FormatDebtDirection(DebtDirection value) => value switch { DebtDirection.Payable => Payable, DebtDirection.Receivable => Receivable, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static DebtDirection ParseDebtDirection(string value) => value switch { Payable => DebtDirection.Payable, Receivable => DebtDirection.Receivable, _ => throw new InvalidOperationException("Unknown stored debt direction: " + value) };
}
