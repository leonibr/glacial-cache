namespace GlacialCache.PostgreSQL.Abstractions;


internal interface IDbMigrationEvent
{
    string Name { get; }

    Task ExecuteAsync();
}