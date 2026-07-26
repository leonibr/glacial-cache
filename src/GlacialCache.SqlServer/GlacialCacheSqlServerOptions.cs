using System.ComponentModel.DataAnnotations;

namespace GlacialCache.SqlServer;

/// <summary>Configuration for the SQL Server cache provider.</summary>
public sealed class GlacialCacheSqlServerOptions
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string SchemaName { get; set; } = "dbo";

    [Required]
    public string TableName { get; set; } = "glacial_cache";

    public bool CreateInfrastructure { get; set; } = true;

    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public TimeSpan? DefaultSlidingExpiration { get; set; }

    public TimeSpan? DefaultAbsoluteExpirationRelativeToNow { get; set; }
}
