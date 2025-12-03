namespace GlacialCache.PostgreSQL.Configuration.Security;


/// <summary>
/// Audit and logging configuration options.
/// </summary>
public class AuditOptions
{
    /// <summary>
    /// Whether to enable audit logging. Default is false.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = false;

    /// <summary>
    /// Whether to log cache access patterns. Default is false.
    /// </summary>
    public bool LogCacheAccessPatterns { get; set; } = false;
}