using FinanceBot.Application;
using FinanceBot.Domain;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Services;

public sealed record AccountCommandResult(bool Handled, string? Response);

public sealed class AccountCommandService(IDbContextFactory<FinanceDbContext> factory, FinanceOptions options)
{
    public async Task<AccountCommandResult> TryHandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        var text = update.Text?.Trim();
        if (string.IsNullOrEmpty(text) || !text.StartsWith('/')) return new(false, null);

        var commandEnd = text.IndexOfAny([' ', '\n', '\t']);
        var command = (commandEnd < 0 ? text : text[..commandEnd]).Split('@')[0].ToLowerInvariant();
        if (command is not "/account" and not "/accounts" and not "/help") return new(false, null);
        if (!options.AllowedTelegramUserIds.Contains(update.UserId)) return new(true, "⛔ У вас нет доступа к этому боту.");

        return command switch
        {
            "/account" => await CreateAsync(update, commandEnd < 0 ? "" : text[(commandEnd + 1)..], ct),
            "/accounts" => await ListAsync(ct),
            _ => new(true, HelpText)
        };
    }

    private async Task<AccountCommandResult> CreateAsync(TelegramUpdate update, string arguments, CancellationToken ct)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return new(true, "Использование: /account <название> <валюта> <cash|bank|other>\nПример: /account Основная карта MYR bank");

        var currency = parts[^2].ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsLetter)) return new(true, "⚠️ Валюта должна быть трёхбуквенным кодом, например MYR, USD или EUR.");
        if (!TryParseType(parts[^1], out var type)) return new(true, "⚠️ Тип счёта: cash (наличные), bank (банк) или other (другое).");
        var name = string.Join(' ', parts[..^2]);

        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Accounts.AnyAsync(x => x.Name == name && x.CurrencyCode == currency, ct))
            return new(true, $"ℹ️ Счёт «{name}» ({currency}) уже существует.");

        var account = new Account { Name = name, CurrencyCode = currency, Type = type, OwnerUserId = null, CreatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return new(true, $"✅ Счёт создан\n\nНазвание: {account.Name}\nВалюта: {account.CurrencyCode}\nТип: {TypeName(account.Type)}\nID счёта: {account.Id}");
    }

    private async Task<AccountCommandResult> ListAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var accounts = await db.Accounts.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        if (accounts.Count == 0) return new(true, "У семьи пока нет счетов. Создайте первый: /account Наличные MYR cash");
        var accountIds = accounts.Select(x => x.Id).ToList();
        var movements = await db.AccountMovements.Where(x => accountIds.Contains(x.AccountId)).Select(x => new { x.AccountId, x.Amount }).ToListAsync(ct);
        var balances = movements.GroupBy(x => x.AccountId).ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var lines = accounts.Select(x => $"• {x.Name} — {balances.GetValueOrDefault(x.Id):0.00} {x.CurrencyCode} ({TypeName(x.Type)}, ID {x.Id})");
        return new(true, $"💳 Общие счета:\n\n{string.Join("\n", lines)}");
    }

    private static bool TryParseType(string value, out AccountType type)
    {
        type = value.ToLowerInvariant() switch { "cash" or "наличные" => AccountType.Cash, "bank" or "банк" => AccountType.Bank, "other" or "другое" => AccountType.Other, _ => default };
        return value.ToLowerInvariant() is "cash" or "наличные" or "bank" or "банк" or "other" or "другое";
    }

    private static string TypeName(AccountType type) => type switch { AccountType.Cash => "Наличные", AccountType.Bank => "Банк", _ => "Другое" };
    private const string HelpText = "Команды:\n/account <название> <валюта> <cash|bank|other> — создать счёт\n/accounts — показать счета и балансы\n\nПример: /account Основная карта MYR bank";
}
