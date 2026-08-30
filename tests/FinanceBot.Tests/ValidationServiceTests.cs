using Xunit;
using FinanceBot.Application;

namespace FinanceBot.Tests;
public sealed class ValidationServiceTests
{
    [Fact] public void Receipt_math_and_items_are_validated()
    {
        var receipt = new ReceiptDraft(100, 6, 10, 0, 0, 116, [new("Food", 1, 100, 100, 0, 6, 10, 116, null, .9m)]);
        var checks = new ValidationService(.01m).Validate(new("Expense", 116, "MYR", "Dinner", null, 1, null, receipt));
        Assert.All(checks, x => Assert.True(x.Passed));
    }
    [Fact] public void Invalid_item_total_fails()
    {
        var receipt = new ReceiptDraft(100, 6, 10, 0, 0, 116, [new("Food", 1, 100, 100, 0, 0, 0, 100, null, .9m)]);
        Assert.Contains(new ValidationService(.05m).Validate(new("Expense", 116, "MYR", "Dinner", null, 1, null, receipt)), x => x.Type == "ItemsTotal" && !x.Passed);
    }
}
