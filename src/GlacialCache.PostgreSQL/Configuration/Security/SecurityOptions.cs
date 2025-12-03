namespace GlacialCache.PostgreSQL.Configuration.Security;

/// <summary>
/// Security and audit configuration options.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// Connection string security configuration.
    /// </summary>
    public ConnectionStringOptions ConnectionString { get; set; } = new();

    /// <summary>
    /// Token and authentication configuration.
    /// </summary>
    public TokenOptions Tokens { get; set; } = new();

    /// <summary>
    /// Audit and logging configuration.
    /// </summary>
    public AuditOptions Audit { get; set; } = new();
}
