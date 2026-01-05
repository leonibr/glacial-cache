using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Polly;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Configuration.Resilience;
using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Configuration.Maintenance;

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
    private readonly GlacialCacheEntryFactory _entryFactory;
    private readonly Mock<IPolicyFactory> _policyFactory = new();
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _optionsMonitor = new();
    private readonly Mock<ISchemaManager> _schemaManager = new();

    public GlacialCachePostgreSQLTests()
    {
        _entryFactory = new GlacialCacheEntryFactory(_serializer.Object);
    }

    private static Exception CreateConnectionException()
    {
        // Create an exception that will be detected as a connection failure
        // IsConnectionFailure checks for InvalidOperationException with message containing "connection"
        // The check is case-sensitive, so we need lowercase "connection" in the message
        return new InvalidOperationException("connection failed: unable to connect to database");
    }
}
