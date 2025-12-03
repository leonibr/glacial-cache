namespace GlacialCache.PostgreSQL.Abstractions;

internal interface IDbNomenclature : IDisposable
{
    string TableName { get; }
    string FullTableName { get; }
    string SchemaName { get; }
}