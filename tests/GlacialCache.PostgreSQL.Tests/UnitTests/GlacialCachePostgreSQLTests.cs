using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Maintenance;
using GlacialCache.PostgreSQL.Configuration.Resilience;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using NpgsqlTypes;
using Polly;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Unit tests for GlacialCachePostgreSQL focusing on configuration options.
/// </summary>
public class GlacialCachePostgreSQLTests
{
    private readonly Mock<ILogger<GlacialCachePostgreSQL>> _logger = new();
    private readonly Mock<ITimeConverterService> _timeConverter = new();
    private readonly Mock<IPostgreSQLDataSource> _dataSource = new();
    private readonly Mock<IDbRawCommands> _dbRawCommands = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<TimeProvider> _timeProvider = new();
    private readonly Mock<ICacheEntrySerializer> _serializer = new();
    private readonly CacheEntryHelper _entryHelper;
    private readonly Mock<IPolicyFactory> _policyFactory = new();
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _optionsMonitor = new();
    private readonly Mock<ISchemaManager> _schemaManager = new();

    public GlacialCachePostgreSQLTests()
    {
        _entryHelper = new CacheEntryHelper(_serializer.Object);
    }

    private static Exception CreateConnectionException()
    {
        // Create an exception that will be detected as a connection failure
        // IsConnectionFailure checks for InvalidOperationException with message containing "connection"
        // The check is case-sensitive, so we need lowercase "connection" in the message
        return new InvalidOperationException("connection failed: unable to connect to database");
    }

    [Fact]
    public void CreateSetMultipleBatchCommand_AddsNowAsSixthParameter()
    {
        // Arrange
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var command = GlacialCachePostgreSQL.CreateSetMultipleBatchCommand(
            "INSERT INTO test VALUES ($1, $2, $6 + $3::interval, $4, $5)",
            "key",
            new byte[] { 1, 2, 3 },
            null,
            TimeSpan.FromMinutes(5),
            null,
            now);

        // Assert
        command.Parameters.Count.ShouldBe(6);
        command.Parameters[2].NpgsqlDbType.ShouldBe(NpgsqlDbType.Interval);
        command.Parameters[2].Value.ShouldBe(DBNull.Value);
        command.Parameters[5].Value.ShouldBe(now);
    }
}
