using System.Text.RegularExpressions;

namespace GlacialCache.SqlServer;

internal static partial class SqlServerIdentifier
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIdentifier();

    public static string Quote(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidIdentifier().IsMatch(value))
            throw new ArgumentException("SQL Server identifiers must start with a letter or underscore and contain only letters, digits, or underscores (maximum 128 characters).", parameterName);

        return $"[{value}]";
    }
}
