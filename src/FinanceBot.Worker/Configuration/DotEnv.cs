namespace FinanceBot.Worker.Configuration;

/// <summary>
/// Loads local development settings from a dotenv file. Real environment variables are
/// registered after these values and therefore always take precedence.
/// </summary>
internal static class DotEnv
{
    public static IReadOnlyDictionary<string, string?> LoadNearest(params string[] startDirectories)
    {
        foreach (var startDirectory in startDirectories)
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, ".env");
                if (File.Exists(path))
                {
                    return Load(path);
                }
            }
        }

        return new Dictionary<string, string?>();
    }

    public static IReadOnlyDictionary<string, string?> Load(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var sourceLine in File.ReadLines(path))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}
