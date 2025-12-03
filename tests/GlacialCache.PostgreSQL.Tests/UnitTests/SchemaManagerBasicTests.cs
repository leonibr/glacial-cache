using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class SchemaManagerBasicTests
{
    [Fact]
    public void SchemaManager_Constructor_InitializesCorrectly()
    {
        // Arrange
        var mockDataSource = new Mock<IPostgreSQLDataSource>();
        var mockNomenclature = new Mock<IDbNomenclature>();
        var mockLogger = new Mock<ILogger<SchemaManager>>();
        var options = new GlacialCachePostgreSQLOptions
        {
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = true
            }
        };

        mockNomenclature.Setup(x => x.SchemaName).Returns("glacial_cache");
        mockNomenclature.Setup(x => x.TableName).Returns("cache");
        mockNomenclature.Setup(x => x.FullTableName).Returns("glacial_cache.cache");

        // Act
        var schemaManager = new SchemaManager(
            mockDataSource.Object,
            options,
            mockLogger.Object,
            mockNomenclature.Object);

        // Assert
        Assert.NotNull(schemaManager);
    }

    [Fact]
    public async Task SchemaManager_EnsureSchemaAsync_CreateInfrastructureFalse_SkipsCreation()
    {
        // Arrange
        var mockDataSource = new Mock<IPostgreSQLDataSource>();
        var mockNomenclature = new Mock<IDbNomenclature>();
        var mockLogger = new Mock<ILogger<SchemaManager>>();
        var options = new GlacialCachePostgreSQLOptions
        {
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = false
            }
        };

        mockNomenclature.Setup(x => x.SchemaName).Returns("glacial_cache");
        mockNomenclature.Setup(x => x.TableName).Returns("cache");
        mockNomenclature.Setup(x => x.FullTableName).Returns("glacial_cache.cache");

        var schemaManager = new SchemaManager(
            mockDataSource.Object,
            options,
            mockLogger.Object,
            mockNomenclature.Object);

        // Act
        await schemaManager.EnsureSchemaAsync();

        // Assert
        mockDataSource.Verify(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipping schema creation - CreateInfrastructure is disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public void GenerateSchemaLockKey_DeterministicAndUnique()
    {
        // Test that same inputs always generate same key
        var key1 = GenerateSchemaLockKey("glacial", "cache");
        var key2 = GenerateSchemaLockKey("glacial", "cache");
        Assert.Equal(key1, key2);

        // Test that different inputs generate different keys
        var key3 = GenerateSchemaLockKey("glacial2", "cache");
        var key4 = GenerateSchemaLockKey("glacial", "cache2");
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(key1, key4);
        Assert.NotEqual(key3, key4);
    }

    private int GenerateSchemaLockKey(string schemaName, string tableName)
    {
        var deterministicString = $"schema_creation_{schemaName}_{tableName}";
        return Math.Abs(deterministicString.GetHashCode());
    }
}
