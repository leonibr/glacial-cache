namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Event arguments for configuration change events.
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// The new configuration.
    /// </summary>
    public GlacialCachePostgreSQLOptions NewConfiguration { get; }

    public ConfigurationChangedEventArgs(GlacialCachePostgreSQLOptions newConfiguration)
    {
        NewConfiguration = newConfiguration;
    }
}