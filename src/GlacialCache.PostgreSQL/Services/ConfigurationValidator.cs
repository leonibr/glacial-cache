using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Services;
using Configuration;

/// <summary>
/// Service for validating GlacialCache PostgreSQL configuration options.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// PostgreSQL maximum identifier length in bytes (63 bytes).
    /// This is a hard limit in PostgreSQL that cannot be changed.
    /// </summary>
    public const int MaxPostgreSqlIdentifierLength = 63;

    /// <summary>
    /// Validates the provided options and throws an exception if validation fails.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="logger">Optional logger for validation warnings.</param>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static void ValidateOptions(GlacialCachePostgreSQLOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validationContext = new ValidationContext(options);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(options, validationContext, validationResults, true))
        {
            var errors = string.Join(", ", validationResults.Select(r => r.ErrorMessage));
            var errorMessage = $"Configuration validation failed: {errors}";

            logger?.LogError("Configuration validation failed: {Errors}", errors);
            throw new ArgumentException(errorMessage, nameof(options));
        }

        // Additional runtime validations
        ValidateConnectionString(options.Connection.ConnectionString, logger);
        ValidateTableAndSchemaNames(options.Cache.TableName, options.Cache.SchemaName, logger);
        ValidateIdentifierLengths(options.Cache.TableName, options.Cache.SchemaName, logger);

        logger?.LogDebug("Configuration validation completed successfully");
    }

    /// <summary>
    /// Validates the connection string format and basic requirements.
    /// </summary>
    private static void ValidateConnectionString(string connectionString, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        // Basic PostgreSQL connection string validation
        var requiredParts = new[] { "Host", "Database", "Username" };
        var missingParts = requiredParts.Where(part =>
            !connectionString.Contains($"{part}=", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (missingParts.Any())
        {
            var missing = string.Join(", ", missingParts);
            var errorMessage = $"Connection string is missing required parts: {missing}";
            logger?.LogError("Connection string validation failed: {Error}", errorMessage);
            throw new ArgumentException(errorMessage, nameof(connectionString));
        }

        // Check for common connection string issues
        if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Connection string uses 'Server=' instead of 'Host='. Consider using 'Host=' for consistency.");
        }
    }

    /// <summary>
    /// Validates table and schema names for PostgreSQL compatibility.
    /// </summary>
    private static void ValidateTableAndSchemaNames(string tableName, string schemaName, ILogger? logger)
    {
        // Validate table name
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
        }

        if (!IsValidPostgreSqlIdentifier(tableName))
        {
            var errorMessage = $"Table name '{tableName}' is not a valid PostgreSQL identifier. Use only letters, numbers, and underscores, starting with a letter or underscore.";
            logger?.LogError("Table name validation failed: {Error}", errorMessage);
            throw new ArgumentException(errorMessage, nameof(tableName));
        }

        // Validate schema name
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new ArgumentException("Schema name cannot be null or empty.", nameof(schemaName));
        }

        if (!IsValidPostgreSqlIdentifier(schemaName))
        {
            var errorMessage = $"Schema name '{schemaName}' is not a valid PostgreSQL identifier. Use only letters, numbers, and underscores, starting with a letter or underscore.";
            logger?.LogError("Schema name validation failed: {Error}", errorMessage);
            throw new ArgumentException(errorMessage, nameof(schemaName));
        }

        // Check for reserved PostgreSQL keywords
        var reservedKeywords = new[] { "user", "order", "group", "table", "schema", "database", "index" };
        if (reservedKeywords.Contains(tableName.ToLowerInvariant()))
        {
            logger?.LogWarning("Table name '{TableName}' is a PostgreSQL reserved keyword. Consider using a different name to avoid potential issues.", tableName);
        }

        if (reservedKeywords.Contains(schemaName.ToLowerInvariant()))
        {
            logger?.LogWarning("Schema name '{SchemaName}' is a PostgreSQL reserved keyword. Consider using a different name to avoid potential issues.", schemaName);
        }
    }

    /// <summary>
    /// Validates identifier lengths for PostgreSQL compatibility, including concatenated names for indexes.
    /// </summary>
    private static void ValidateIdentifierLengths(string tableName, string schemaName, ILogger? logger)
    {
        // Validate individual identifier lengths
        ValidateSingleIdentifierLength("Table name", tableName, logger);
        ValidateSingleIdentifierLength("Schema name", schemaName, logger);

        // Validate concatenated identifiers for indexes
        ValidateIndexNameLengths(tableName, schemaName, logger);
    }

    /// <summary>
    /// Validates a single identifier's length against PostgreSQL limits.
    /// </summary>
    private static void ValidateSingleIdentifierLength(string identifierType, string identifier, ILogger? logger)
    {
        var byteLength = System.Text.Encoding.UTF8.GetByteCount(identifier);
        if (byteLength > MaxPostgreSqlIdentifierLength)
        {
            var errorMessage = $"{identifierType} '{identifier}' is {byteLength} bytes long, which exceeds PostgreSQL's maximum identifier length of {MaxPostgreSqlIdentifierLength} bytes.";
            logger?.LogError("Identifier length validation failed: {Error}", errorMessage);
            throw new ArgumentException(errorMessage, nameof(identifier));
        }

        logger?.LogDebug("{IdentifierType} '{Identifier}' length: {ByteLength} bytes (max: {MaxLength})",
            identifierType, identifier, byteLength, MaxPostgreSqlIdentifierLength);
    }

    /// <summary>
    /// Validates index name lengths that are created by concatenating identifiers.
    /// </summary>
    private static void ValidateIndexNameLengths(string tableName, string schemaName, ILogger? logger)
    {
        // Define all index naming patterns used in the codebase
        var indexPatterns = new[]
        {
            // From 0001_InitialMigration.cs
            $"idx_{tableName}_val_type",
            $"idx_{tableName}_val_size",
            $"idx_{tableName}_next_exp",
            
            // From DbMigrationTracker.cs
            $"idx_{schemaName}_mig_name",
            $"idx_{schemaName}_mig_applied",
            $"idx_{schemaName}_mig_env"
        };

        foreach (var indexPattern in indexPatterns)
        {
            var length = indexPattern.Length;
            if (length > MaxPostgreSqlIdentifierLength)
            {
                var errorMessage = $"Index name '{indexPattern}' would be {length} characters long, which exceeds PostgreSQL's maximum identifier length of {MaxPostgreSqlIdentifierLength} characters. " +
                                 $"Consider using shorter table name ({tableName}) or schema name ({schemaName}).";
                logger?.LogError("Index name length validation failed: {Error}", errorMessage);
                throw new ArgumentException(errorMessage, nameof(tableName));
            }

            logger?.LogDebug("Index pattern '{IndexPattern}' length: {ByteLength} bytes (max: {MaxLength})",
                indexPattern, length, MaxPostgreSqlIdentifierLength);
        }

        // Also validate the full table name (schema.table)
        var fullTableName = $"{schemaName}.{tableName}";
        var fullTableNameByteLength = System.Text.Encoding.UTF8.GetByteCount(fullTableName);
        logger?.LogDebug("Full table name '{FullTableName}' length: {ByteLength} bytes",
            fullTableName, fullTableNameByteLength);
    }

    /// <summary>
    /// Checks if a string is a valid PostgreSQL identifier.
    /// </summary>
    private static bool IsValidPostgreSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // PostgreSQL identifiers must start with a letter or underscore
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            return false;

        // Subsequent characters can be letters, numbers, or underscores
        return identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// Validates options and returns validation results without throwing exceptions.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>A collection of validation results.</returns>
    public static IEnumerable<ValidationResult> ValidateOptionsNonThrowing(GlacialCachePostgreSQLOptions options)
    {
        if (options == null)
        {
            yield return new ValidationResult("Options cannot be null");
            yield break;
        }

        var validationContext = new ValidationContext(options);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(options, validationContext, validationResults, true))
        {
            foreach (var result in validationResults)
            {
                yield return result;
            }
        }

        // Additional custom validations
        if (string.IsNullOrWhiteSpace(options.Connection.ConnectionString))
        {
            yield return new ValidationResult("Connection string cannot be null or empty", new[] { nameof(options.Connection.ConnectionString) });
        }

        if (!IsValidPostgreSqlIdentifier(options.Cache.TableName))
        {
            yield return new ValidationResult("Table name is not a valid PostgreSQL identifier", new[] { nameof(options.Cache.TableName) });
        }

        if (!IsValidPostgreSqlIdentifier(options.Cache.SchemaName))
        {
            yield return new ValidationResult("Schema name is not a valid PostgreSQL identifier", new[] { nameof(options.Cache.SchemaName) });
        }

        // Length validations
        var tableNameByteLength = System.Text.Encoding.UTF8.GetByteCount(options.Cache.TableName);
        if (tableNameByteLength > MaxPostgreSqlIdentifierLength)
        {
            yield return new ValidationResult(
                $"Table name '{options.Cache.TableName}' is {tableNameByteLength} bytes long, which exceeds PostgreSQL's maximum identifier length of {MaxPostgreSqlIdentifierLength} bytes",
                new[] { nameof(options.Cache.TableName) });
        }

        var schemaNameByteLength = System.Text.Encoding.UTF8.GetByteCount(options.Cache.SchemaName);
        if (schemaNameByteLength > MaxPostgreSqlIdentifierLength)
        {
            yield return new ValidationResult(
                $"Schema name '{options.Cache.SchemaName}' is {schemaNameByteLength} bytes long, which exceeds PostgreSQL's maximum identifier length of {MaxPostgreSqlIdentifierLength} bytes",
                new[] { nameof(options.Cache.SchemaName) });
        }

        // Index name length validations
        var indexPatterns = new[]
        {
            $"idx_{options.Cache.TableName}_val_type",
            $"idx_{options.Cache.TableName}_val_size",
            $"idx_{options.Cache.TableName}_next_exp",
            $"idx_{options.Cache.SchemaName}_mig_name",
            $"idx_{options.Cache.SchemaName}_mig_applied",
            $"idx_{options.Cache.SchemaName}_mig_env"
        };

        foreach (var indexPattern in indexPatterns)
        {
            var byteLength = System.Text.Encoding.UTF8.GetByteCount(indexPattern);
            if (byteLength > MaxPostgreSqlIdentifierLength)
            {
                yield return new ValidationResult(
                    $"Index name '{indexPattern}' would be {byteLength} bytes long, which exceeds PostgreSQL's maximum identifier length of {MaxPostgreSqlIdentifierLength} bytes. Consider using shorter table or schema names.",
                    new[] { nameof(options.Cache.TableName), nameof(options.Cache.SchemaName) });
            }
        }
    }
}