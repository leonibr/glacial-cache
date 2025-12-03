using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class DbNomenclatureTests
{
    private readonly Mock<ILogger<DbNomenclature>> _mockLogger;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _mockOptionsMonitor;

    public DbNomenclatureTests()
    {
        _mockLogger = new Mock<ILogger<DbNomenclature>>();
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _options = CreateTestOptions();

        // Set up mock IOptionsMonitor
        _mockOptionsMonitor = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        _mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(_options);

        // Initialize observable properties through SetLogger methods
        _options.Cache.SetLogger(_mockLogger.Object);
        _options.Connection.SetLogger(_mockLogger.Object);
    }

    private GlacialCachePostgreSQLOptions CreateTestOptions()
    {
        return new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                TableName = "test_cache",
                SchemaName = "test_schema"
            },
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = true
            }
        };
    }

    [Fact]
    public void DbNomenclature_Properties_AreCorrectlySet()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                TableName = "TestTable",
                SchemaName = "TestSchema"
            },
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = true
            }
        };

        var mockOptionsMonitor = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        // Initialize observable properties through SetLogger methods
        options.Cache.SetLogger(_mockLogger.Object);
        options.Connection.SetLogger(_mockLogger.Object);

        // Act
        var nomenclature = new DbNomenclature(mockOptionsMonitor.Object, _mockLogger.Object);

        // Assert
        Assert.Equal("testtable", nomenclature.TableName);
        Assert.Equal("testschema", nomenclature.SchemaName);
        Assert.Equal("testschema.testtable", nomenclature.FullTableName);
    }

    [Fact]
    public void DbNomenclature_Properties_HandleCaseSensitivity()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Cache = new CacheOptions
            {
                TableName = "MIXED_CASE_TABLE",
                SchemaName = "MixedCaseSchema"
            },
            Infrastructure = new InfrastructureOptions
            {
                CreateInfrastructure = true
            }
        };

        var mockOptionsMonitor = new Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        // Initialize observable properties through SetLogger methods
        options.Cache.SetLogger(_mockLogger.Object);
        options.Connection.SetLogger(_mockLogger.Object);

        // Act
        var nomenclature = new DbNomenclature(mockOptionsMonitor.Object, _mockLogger.Object);

        // Assert - All properties should be converted to lowercase
        Assert.Equal("mixed_case_table", nomenclature.TableName);
        Assert.Equal("mixedcaseschema", nomenclature.SchemaName);
        Assert.Equal("mixedcaseschema.mixed_case_table", nomenclature.FullTableName);
    }

    [Fact]
    public void DbNomenclature_Dispose_UnregistersChangeHandlers()
    {
        // Arrange
        var nomenclature = new DbNomenclature(_mockOptionsMonitor.Object, _mockLogger.Object);

        // Act & Assert - Should not throw and should properly dispose
        nomenclature.Dispose();

        // Verify it can be disposed multiple times without issues
        nomenclature.Dispose();
    }

    [Fact]
    public void DbNomenclature_ObservableProperties_TriggerUpdates()
    {
        // Arrange
        var nomenclature = new DbNomenclature(_mockOptionsMonitor.Object, _mockLogger.Object);
        var initialTableName = nomenclature.TableName;

        // Act - Change observable property value
        _options.Cache.TableNameObservable.Value = "new_table_name";

        // Assert - The nomenclature should have been updated
        Assert.NotEqual(initialTableName, nomenclature.TableName);
        Assert.Equal("new_table_name", nomenclature.TableName);
        Assert.Equal("test_schema.new_table_name", nomenclature.FullTableName);
    }

    [Fact]
    public void DbNomenclature_SchemaChange_UpdatesFullTableName()
    {
        // Arrange
        var nomenclature = new DbNomenclature(_mockOptionsMonitor.Object, _mockLogger.Object);
        var initialFullTableName = nomenclature.FullTableName;

        // Act - Change schema name via observable property
        _options.Cache.SchemaNameObservable.Value = "new_schema";

        // Assert - The full table name should be updated
        Assert.NotEqual(initialFullTableName, nomenclature.FullTableName);
        Assert.Equal("new_schema", nomenclature.SchemaName);
        Assert.Equal("new_schema.test_cache", nomenclature.FullTableName);
    }
}
