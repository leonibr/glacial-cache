namespace GlacialCache.PostgreSQL.Configuration.Security;


/// <summary>
/// Connection string security configuration options.
/// </summary>
public class ConnectionStringOptions
{
    /// <summary>
    /// Whether to mask sensitive information in logs. Default is true.
    /// </summary>
    public bool MaskInLogs { get; set; } = true;

    /// <summary>
    /// Array of sensitive parameter names to mask in logs.
    /// </summary>
    public string[] SensitiveParameters { get; set; } = { "Password", "Token", "Key" };
}
