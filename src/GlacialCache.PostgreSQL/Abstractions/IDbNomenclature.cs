namespace GlacialCache.PostgreSQL.Abstractions;

internal interface IDbNomenclature : IDisposable
{
    /// <summary>
    /// The table name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// The fully qualified table name (schema.table).
    /// </summary>
    string FullTableName { get; }

    /// <summary>
    /// The schema name (lowercase, validated PostgreSQL identifier).
    /// </summary>
    string SchemaName { get; }
}
