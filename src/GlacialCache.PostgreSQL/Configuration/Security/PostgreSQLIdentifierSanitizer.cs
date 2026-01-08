using System.Text;
using System.Text.RegularExpressions;

namespace GlacialCache.PostgreSQL.Configuration.Security;

/// <summary>
/// Provides sanitization and validation for PostgreSQL identifiers (schema names, table names, etc.)
/// to prevent SQL injection attacks in DDL statements.
/// </summary>
public static partial class PostgreSQLIdentifierSanitizer
{
    /// <summary>
    /// Maximum length for PostgreSQL identifiers in bytes.
    /// </summary>
    public const int MaxIdentifierLength = 63;

    /// <summary>
    /// SQL keywords that are not allowed as identifiers.
    /// </summary>
    private static readonly HashSet<string> DangerousKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DROP", "DELETE", "UPDATE", "INSERT", "SELECT",
        "ALTER", "CREATE", "TRUNCATE", "EXEC", "EXECUTE",
        "GRANT", "REVOKE", "COMMIT", "ROLLBACK", "TRANSACTION"
    };

    /// <summary>
    /// Zero-width Unicode characters that could be used for obfuscation.
    /// </summary>
    private static readonly char[] ZeroWidthCharacters =
    [
        '\u200B', // Zero-width space
        '\u200C', // Zero-width non-joiner
        '\u200D', // Zero-width joiner
        '\uFEFF'  // Byte order mark / zero-width no-break space
    ];

    /// <summary>
    /// Validates and sanitizes a PostgreSQL identifier, returning a quoted identifier ready for SQL use.
    /// </summary>
    /// <param name="identifier">The raw identifier to sanitize.</param>
    /// <param name="maxLength">Maximum byte length (default 63 for PostgreSQL).</param>
    /// <returns>A quoted, sanitized identifier safe for use in SQL statements.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier is invalid.</exception>
    public static string SanitizeIdentifier(string identifier, int maxLength = MaxIdentifierLength)
    {
        // Validate and normalize to lowercase
        var validated = ValidateAndNormalize(identifier, maxLength);

        // Return quoted identifier (ready for SQL use)
        return QuoteIdentifier(validated);
    }

    /// <summary>
    /// Validates that a raw identifier (without quoting) is valid for PostgreSQL.
    /// Does not quote the identifier - use this for validation-only scenarios.
    /// </summary>
    /// <param name="identifier">The identifier to validate.</param>
    /// <param name="maxLength">Maximum byte length (default 63 for PostgreSQL).</param>
    /// <returns>True if the identifier is valid; false otherwise.</returns>
    public static bool IsValidIdentifier(string identifier, int maxLength = MaxIdentifierLength)
    {
        try
        {
            ValidateAndNormalize(identifier, maxLength);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a PostgreSQL identifier and returns it normalized (lowercase).
    /// PostgreSQL treats unquoted identifiers as lowercase, so we normalize to lowercase.
    /// </summary>
    /// <param name="identifier">The raw identifier to validate.</param>
    /// <param name="maxLength">Maximum byte length (default 63 for PostgreSQL).</param>
    /// <returns>The validated, lowercased identifier (unquoted).</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier is invalid.</exception>
    public static string ValidateAndNormalize(string identifier, int maxLength = MaxIdentifierLength)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be null, empty, or whitespace.", nameof(identifier));

        // Normalize Unicode to prevent homoglyph attacks
        var normalized = identifier.Normalize(NormalizationForm.FormC);

        // Validate UTF-8 encoding and check byte length
        ValidateUtf8Encoding(normalized, maxLength);

        // Check for control characters
        ValidateNoControlCharacters(normalized);

        // Check for zero-width characters (potential obfuscation)
        ValidateNoZeroWidthCharacters(normalized);

        // Check for SQL comment patterns
        ValidateNoCommentPatterns(normalized);

        // Check for dangerous SQL keywords
        ValidateNotDangerousKeyword(normalized);

        // Validate identifier pattern (ASCII alphanumeric and underscores only)
        ValidateIdentifierPattern(normalized);

        // Return lowercase - PostgreSQL treats unquoted identifiers as lowercase
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Quotes a PostgreSQL identifier by wrapping it in double quotes and escaping internal quotes.
    /// </summary>
    /// <param name="identifier">The identifier to quote.</param>
    /// <returns>The quoted identifier.</returns>
    private static string QuoteIdentifier(string identifier)
    {
        // Escape any internal double quotes by doubling them
        var escaped = identifier.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static void ValidateUtf8Encoding(string identifier, int maxLength)
    {
        try
        {
            var utf8Bytes = Encoding.UTF8.GetBytes(identifier);

            // PostgreSQL identifier length limit is 63 BYTES, not characters
            if (utf8Bytes.Length > maxLength)
                throw new ArgumentException(
                    $"Identifier byte length ({utf8Bytes.Length}) cannot exceed {maxLength} bytes. " +
                    $"Consider using a shorter name.", nameof(identifier));
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Identifier contains invalid UTF-8 encoding.", nameof(identifier));
        }
    }

    private static void ValidateNoControlCharacters(string identifier)
    {
        foreach (var c in identifier)
        {
            if (char.IsControl(c))
                throw new ArgumentException(
                    $"Identifier cannot contain control characters. Found: U+{(int)c:X4}", nameof(identifier));
        }
    }

    private static void ValidateNoZeroWidthCharacters(string identifier)
    {
        foreach (var c in identifier)
        {
            if (ZeroWidthCharacters.Contains(c))
                throw new ArgumentException(
                    $"Identifier cannot contain zero-width characters (potential obfuscation). Found: U+{(int)c:X4}",
                    nameof(identifier));
        }
    }

    private static void ValidateNoCommentPatterns(string identifier)
    {
        if (identifier.Contains("--", StringComparison.Ordinal))
            throw new ArgumentException(
                "Identifier cannot contain SQL line comment pattern '--'.", nameof(identifier));

        if (identifier.Contains("/*", StringComparison.Ordinal))
            throw new ArgumentException(
                "Identifier cannot contain SQL block comment start pattern '/*'.", nameof(identifier));

        if (identifier.Contains("*/", StringComparison.Ordinal))
            throw new ArgumentException(
                "Identifier cannot contain SQL block comment end pattern '*/'.", nameof(identifier));
    }

    private static void ValidateNotDangerousKeyword(string identifier)
    {
        if (DangerousKeywords.Contains(identifier))
            throw new ArgumentException(
                $"Identifier cannot be a SQL keyword: '{identifier}'.", nameof(identifier));
    }

    private static void ValidateIdentifierPattern(string identifier)
    {
        // PostgreSQL identifier validation: only allow ASCII alphanumeric and underscores
        // This prevents Unicode homoglyph attacks by restricting to ASCII only
        // Must start with letter or underscore
        if (!IdentifierRegex().IsMatch(identifier))
            throw new ArgumentException(
                $"Invalid PostgreSQL identifier: '{identifier}'. " +
                "Must start with an ASCII letter or underscore and contain only ASCII alphanumeric characters and underscores.",
                nameof(identifier));
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
}
