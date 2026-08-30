namespace FinanceBot.Application;

public sealed class ValidationService(decimal tolerance)
{
    public IReadOnlyList<ValidationCheck> Validate(ExtractedOperation operation)
    {
        var checks = new List<ValidationCheck>();
        if (operation.Amount < 0) checks.Add(Check("PositiveAmount", 0, operation.Amount, false, "Amount must not be negative."));
        if (operation.Receipt is { } receipt)
        {
            var calculated = receipt.Subtotal + receipt.Tax + receipt.ServiceCharge - receipt.Discount + receipt.Rounding;
            checks.Add(Check("ReceiptTotal", receipt.Total, calculated, Close(receipt.Total, calculated), "Receipt components must equal total."));
            var itemTotal = receipt.Items.Sum(x => x.FinalAmount);
            checks.Add(Check("ItemsTotal", receipt.Total, itemTotal, Close(receipt.Total, itemTotal), "Item totals must equal receipt total."));
            checks.Add(Check("TransactionTotal", operation.Amount, receipt.Total, Close(operation.Amount, receipt.Total), "Transaction amount must equal receipt total."));
        }
        return checks;
    }
    public ValidationCheck ValidateMovements(ExtractedOperation operation, IReadOnlyCollection<decimal> movements)
    {
        var actual = movements.Sum();
        var expected = operation.Kind switch { "Expense" or "DebtSettlement" => -operation.Amount, "Income" => operation.Amount, "Transfer" when operation.Currency == "SAME" => 0, _ => actual };
        return Check("AccountMovements", expected, actual, Close(expected, actual), "Movement sign/sum is inconsistent with operation type.");
    }
    private bool Close(decimal a, decimal b) => Math.Abs(a - b) <= tolerance;
    private static ValidationCheck Check(string type, decimal expected, decimal actual, bool pass, string message) => new(type, expected, actual, pass, message);
}
