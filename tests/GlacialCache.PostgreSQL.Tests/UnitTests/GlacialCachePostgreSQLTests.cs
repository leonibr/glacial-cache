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
using System.Buffers;
using System.Runtime.InteropServices;

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

    [Fact]
    public void CreateSetMultipleDirectBatchCommand_RetainsSliceAndUsesBytea()
    {
        // Arrange
        var backingBuffer = new byte[] { 0, 1, 2, 3, 0 };
        ReadOnlyMemory<byte> value = backingBuffer.AsMemory(1, 3);

        // Act
        var command = GlacialCachePostgreSQL.CreateSetMultipleDirectBatchCommand(
            "INSERT INTO test VALUES ($1, $2, $6 + $3::interval, $4, $5)",
            "key",
            value,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        // Assert
        command.Parameters[1].NpgsqlDbType.ShouldBe(NpgsqlDbType.Bytea);
        command.Parameters[1].Value.ShouldBeOfType<ReadOnlyMemory<byte>>().Span.SequenceEqual(value.Span).ShouldBeTrue();

        backingBuffer[2] = 9;
        command.Parameters[1].Value.ShouldBeOfType<ReadOnlyMemory<byte>>().Span[1].ShouldBe((byte)9);
    }

    [Fact]
    public void CopyToSnapshotArena_PreservesSlicesOrderingAndConstructionSnapshot()
    {
        var firstBacking = new byte[] { 0, 1, 2, 3, 0 };
        using var owner = new NonArrayMemoryManager(new byte[] { 4, 5, 6 });
        var arena = new byte[6];
        var offset = 0;
        MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)owner.Memory, out ArraySegment<byte> _).ShouldBeFalse();

        var first = GlacialCachePostgreSQL.CopyToSnapshotArena(firstBacking.AsMemory(1, 3), arena, ref offset);
        var second = GlacialCachePostgreSQL.CopyToSnapshotArena(owner.Memory[..3], arena, ref offset);
        var firstCommand = GlacialCachePostgreSQL.CreateSetMultipleSnapshotBatchCommand(
            "INSERT INTO test VALUES ($1, $2, $6 + $3::interval, $4, $5)",
            "first",
            first,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
        var secondCommand = GlacialCachePostgreSQL.CreateSetMultipleSnapshotBatchCommand(
            "INSERT INTO test VALUES ($1, $2, $6 + $3::interval, $4, $5)",
            "second",
            second,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        firstBacking[2] = 9;
        owner.Memory.Span[1] = 9;

        firstCommand.Parameters[0].Value.ShouldBe("first");
        firstCommand.Parameters[1].Value.ShouldBeOfType<ReadOnlyMemory<byte>>().ToArray().ShouldBe(new byte[] { 1, 2, 3 });
        secondCommand.Parameters[0].Value.ShouldBe("second");
        secondCommand.Parameters[1].Value.ShouldBeOfType<ReadOnlyMemory<byte>>().ToArray().ShouldBe(new byte[] { 4, 5, 6 });
        offset.ShouldBe(6);
        MemoryMarshal.TryGetArray(first, out var firstSegment).ShouldBeTrue();
        MemoryMarshal.TryGetArray(second, out var secondSegment).ShouldBeTrue();
        ReferenceEquals(firstSegment.Array, secondSegment.Array).ShouldBeTrue();
        firstSegment.Offset.ShouldBe(0);
        secondSegment.Offset.ShouldBe(3);
    }

    [Fact]
    public void CreateSetMultipleSnapshotBatchCommand_UsesTypedValueParameters()
    {
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var command = GlacialCachePostgreSQL.CreateSetMultipleSnapshotBatchCommand(
            "INSERT INTO test VALUES ($1, $2, $6 + $3::interval, $4, $5)",
            "key",
            new byte[] { 1, 2, 3 },
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            null,
            now);

        command.Parameters[1].ShouldBeOfType<NpgsqlParameter<ReadOnlyMemory<byte>>>();
        command.Parameters[2].ShouldBeOfType<NpgsqlParameter<TimeSpan>>();
        command.Parameters[3].ShouldBeOfType<NpgsqlParameter<TimeSpan>>();
        command.Parameters[5].ShouldBeOfType<NpgsqlParameter<DateTimeOffset>>();
    }

    private sealed class NonArrayMemoryManager(byte[] buffer) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => buffer;

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
