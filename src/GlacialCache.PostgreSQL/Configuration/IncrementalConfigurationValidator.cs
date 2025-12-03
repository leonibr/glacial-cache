using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Configuration;
using Infrastructure;

/// <summary>
/// Provides incremental configuration validation with caching for better performance.
/// </summary>
public class IncrementalConfigurationValidator
{
    private readonly Dictionary<string, ValidationResult[]> _validationCache = new();
    private readonly ReaderWriterLockSlim _validationLock = new();
    private readonly ILogger<IncrementalConfigurationValidator> _logger;

    public IncrementalConfigurationValidator(ILogger<IncrementalConfigurationValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates the entire configuration or a specific section.
    /// </summary>
    public ValidationResult[] ValidateConfiguration(GlacialCachePostgreSQLOptions options, string? section = null)
    {
        var cacheKey = section ?? "full";

        _validationLock.EnterReadLock();
        try
        {
            if (_validationCache.TryGetValue(cacheKey, out var cachedResults))
            {
                _logger.LogDebug("Using cached validation results for section: {Section}", section ?? "full");
                return cachedResults;
            }
        }
        finally
        {
            _validationLock.ExitReadLock();
        }

        _validationLock.EnterWriteLock();
        try
        {
            var results = section == null ?
                options.Validate(new ValidationContext(options)).ToArray() :
                ValidateSection(options, section);

            _validationCache[cacheKey] = results;
            _logger.LogDebug("Cached validation results for section: {Section}, found {ErrorCount} errors",
                section ?? "full", results.Length);

            return results;
        }
        finally
        {
            _validationLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Validates a specific section of the configuration.
    /// </summary>
    private ValidationResult[] ValidateSection(GlacialCachePostgreSQLOptions options, string section)
    {
        var results = new List<ValidationResult>();

        switch (section.ToLowerInvariant())
        {
            case "connection":
                results.AddRange(ValidateConnectionSection(options));
                break;
            case "cache":
                results.AddRange(ValidateCacheSection(options));
                break;
            case "maintenance":
                results.AddRange(ValidateMaintenanceSection(options));
                break;
            case "resilience":
                results.AddRange(ValidateResilienceSection(options));
                break;
            case "infrastructure":
                results.AddRange(ValidateInfrastructureSection(options));
                break;
            case "monitoring":
                results.AddRange(ValidateMonitoringSection(options));
                break;
            default:
                _logger.LogWarning("Unknown validation section: {Section}", section);
                break;
        }

        return results.ToArray();
    }

    private IEnumerable<ValidationResult> ValidateConnectionSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();

        if (options.Connection.Pool.MinSize > options.Connection.Pool.MaxSize)
        {
            results.Add(new ValidationResult(
                "Min connection pool size cannot be greater than max connection pool size",
                new[] { "Connection.Pool.MinSize", "Connection.Pool.MaxSize" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateCacheSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();

        if (string.IsNullOrWhiteSpace(options.Cache.TableName))
        {
            results.Add(new ValidationResult(
                "Cache table name is required",
                new[] { "Cache.TableName" }));
        }

        if (string.IsNullOrWhiteSpace(options.Cache.SchemaName))
        {
            results.Add(new ValidationResult(
                "Cache schema name is required",
                new[] { "Cache.SchemaName" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateMaintenanceSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();

        if (options.Maintenance.CleanupInterval <= TimeSpan.Zero)
        {
            results.Add(new ValidationResult(
                "Cleanup interval must be positive",
                new[] { "Maintenance.CleanupInterval" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateResilienceSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();

        if (options.Resilience.Retry.MaxAttempts < 0)
        {
            results.Add(new ValidationResult(
                "Max retry attempts cannot be negative",
                new[] { "Resilience.Retry.MaxAttempts" }));
        }

        return results;
    }

    private IEnumerable<ValidationResult> ValidateInfrastructureSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();

        // Infrastructure validation - simplified without migration system
        // No additional validation needed for the simplified infrastructure options

        return results;
    }

    private IEnumerable<ValidationResult> ValidateMonitoringSection(GlacialCachePostgreSQLOptions options)
    {
        var results = new List<ValidationResult>();


        return results;
    }

    /// <summary>
    /// Clears the validation cache.
    /// </summary>
    public void ClearValidationCache()
    {
        _validationLock.EnterWriteLock();
        try
        {
            _validationCache.Clear();
            _logger.LogDebug("Cleared validation cache");
        }
        finally
        {
            _validationLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes a specific section from the validation cache.
    /// </summary>
    public void RemoveFromValidationCache(string section)
    {
        _validationLock.EnterWriteLock();
        try
        {
            if (_validationCache.Remove(section))
            {
                _logger.LogDebug("Removed section from validation cache: {Section}", section);
            }
        }
        finally
        {
            _validationLock.ExitWriteLock();
        }
    }
}