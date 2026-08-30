using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceBot.Application;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Services;

public sealed class AnalyticsQueryExecutor(IDbContextFactory<FinanceDbContext> factory) : IAnalyticsQueryExecutor
{
    private const int MaxRows = 200;
    private static readonly Regex Forbidden = new(@"(;|--|/\*|\b(pragma|attach|detach|insert|update|delete|drop|alter|create|replace|vacuum|reindex)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<string> ExecuteAsync(string sql, long userId, CancellationToken ct)
    {
        var normalized = sql.Trim();
        if (!(normalized.StartsWith("select", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("with", StringComparison.OrdinalIgnoreCase)) ||
            Forbidden.IsMatch(normalized) || !normalized.Contains("$userId", StringComparison.Ordinal))
            throw new InvalidOperationException("Analytics SQL must be one parameterized, read-only SELECT.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = normalized;
        command.CommandTimeout = 15;
        var parameter = command.CreateParameter(); parameter.ParameterName="$userId"; parameter.Value=userId; command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<Dictionary<string, object?>>();
        while (rows.Count < MaxRows && await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i=0; i<reader.FieldCount; i++) row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return JsonSerializer.Serialize(new { rows, truncated=rows.Count==MaxRows });
    }
}
