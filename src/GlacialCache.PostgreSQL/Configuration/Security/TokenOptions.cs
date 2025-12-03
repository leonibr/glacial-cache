namespace GlacialCache.PostgreSQL.Configuration.Security;

/// <summary>
/// Token and authentication configuration options.
/// </summary>
public class TokenOptions
{
    /// <summary>
    /// Whether to encrypt tokens in memory. Default is false.
    /// </summary>
    public bool EncryptInMemory { get; set; } = false;

    /// <summary>
    /// Buffer time before token expiration to refresh. Default is 5 minutes.
    /// </summary>
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(5);
}
