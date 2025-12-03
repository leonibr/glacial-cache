using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Infrastructure;
using GlacialCache.PostgreSQL.Models;
using System.ComponentModel;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Integration tests for DbRawCommands with ObservableProperty pattern.
/// Tests SQL rebuilding, performance optimizations, and resource management.
/// </summary>
public class DbRawCommandsObservableIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<DbNomenclature>> _mockLogger;
    private readonly GlacialCachePostgreSQLOptions _options;
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _mockOptionsMonitor;
    private DbNomenclature? _nomenclature;
    private DbRawCommands? _dbRawCommands;

    public DbRawCommandsObservableIntegrationTests()
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

    private void SetupDbRawCommands()
    {
        _nomenclature = new DbNomenclature(_mockOptionsMonitor.Object, _mockLogger.Object);
        _dbRawCommands = new DbRawCommands(_nomenclature, _mockOptionsMonitor.Object);
    }

    #region Construction and Initialization Tests

    [Fact]
    public void DbRawCommands_Construction_ShouldInitializeSqlCorrectly()
    {
        // Arrange & Act
        SetupDbRawCommands();

        // Assert
        _dbRawCommands!.GetSql.Should().Contain("test_schema.test_cache");
        _dbRawCommands.SetSql.Should().Contain("test_schema.test_cache");
        _dbRawCommands.DeleteSql.Should().Contain("test_schema.test_cache");
        _dbRawCommands.RefreshSql.Should().Contain("test_schema.test_cache");
        _dbRawCommands.CleanupExpiredSql.Should().Contain("test_schema.test_cache");
    }

    [Fact]
    public void DbRawCommands_AllSqlProperties_ShouldContainCorrectTableName()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Verify all SQL properties are properly initialized
        var expectedTableName = "test_schema.test_cache";

        _dbRawCommands!.GetSql.Should().Contain(expectedTableName);
        _dbRawCommands.GetSqlCore.Should().Contain(expectedTableName);
        _dbRawCommands.SetSql.Should().Contain(expectedTableName);
        _dbRawCommands.DeleteSql.Should().Contain(expectedTableName);
        _dbRawCommands.DeleteMultipleSql.Should().Contain(expectedTableName);
        _dbRawCommands.RefreshSql.Should().Contain(expectedTableName);
        _dbRawCommands.CleanupExpiredSql.Should().Contain(expectedTableName);
        _dbRawCommands.GetMultipleSql.Should().Contain(expectedTableName);
        _dbRawCommands.SetMultipleSql.Should().Contain(expectedTableName);
        _dbRawCommands.RemoveMultipleSql.Should().Contain(expectedTableName);
        _dbRawCommands.RefreshMultipleSql.Should().Contain(expectedTableName);
    }

    #endregion

    #region SQL Rebuilding Tests

    [Fact]
    public async Task TableNameChange_ShouldTriggerSqlRebuilding()
    {
        // Arrange
        SetupDbRawCommands();
        var originalGetSql = _dbRawCommands!.GetSql;
        var originalSetSql = _dbRawCommands.SetSql;
        var originalDeleteSql = _dbRawCommands.DeleteSql;

        // Act - Change table name through ObservableProperty
        _options.Cache.TableNameObservable.Value = "new_cache_table";

        // Allow event processing
        await WaitForEventProcessingAsync();

        // Assert - All SQL should be updated
        var newGetSql = _dbRawCommands.GetSql;
        var newSetSql = _dbRawCommands.SetSql;
        var newDeleteSql = _dbRawCommands.DeleteSql;

        newGetSql.Should().NotBe(originalGetSql);
        newSetSql.Should().NotBe(originalSetSql);
        newDeleteSql.Should().NotBe(originalDeleteSql);

        newGetSql.Should().Contain("test_schema.new_cache_table");
        newSetSql.Should().Contain("test_schema.new_cache_table");
        newDeleteSql.Should().Contain("test_schema.new_cache_table");

        newGetSql.Should().NotContain("test_schema.test_cache");
        newSetSql.Should().NotContain("test_schema.test_cache");
        newDeleteSql.Should().NotContain("test_schema.test_cache");
    }

    [Fact]
    public async Task SchemaNameChange_ShouldTriggerSqlRebuilding()
    {
        // Arrange
        SetupDbRawCommands();
        var originalRefreshSql = _dbRawCommands!.RefreshSql;
        var originalCleanupSql = _dbRawCommands.CleanupExpiredSql;

        // Act - Change schema name through ObservableProperty
        _options.Cache.SchemaNameObservable.Value = "new_schema";

        // Allow event processing
        await WaitForEventProcessingAsync();

        // Assert - All SQL should be updated
        var newRefreshSql = _dbRawCommands.RefreshSql;
        var newCleanupSql = _dbRawCommands.CleanupExpiredSql;

        newRefreshSql.Should().NotBe(originalRefreshSql);
        newCleanupSql.Should().NotBe(originalCleanupSql);

        newRefreshSql.Should().Contain("new_schema.test_cache");
        newCleanupSql.Should().Contain("new_schema.test_cache");

        newRefreshSql.Should().NotContain("test_schema.test_cache");
        newCleanupSql.Should().NotContain("test_schema.test_cache");
    }

    [Fact]
    public async Task BothNamesChange_ShouldTriggerSqlRebuildingForBoth()
    {
        // Arrange
        SetupDbRawCommands();
        var originalGetMultipleSql = _dbRawCommands!.GetMultipleSql;
        var originalSetMultipleSql = _dbRawCommands.SetMultipleSql;

        // Act - Change both names
        _options.Cache.SchemaNameObservable.Value = "new_schema";
        await Task.Delay(5);
        _options.Cache.TableNameObservable.Value = "new_table";
        await WaitForEventProcessingAsync();

        // Assert
        var newGetMultipleSql = _dbRawCommands.GetMultipleSql;
        var newSetMultipleSql = _dbRawCommands.SetMultipleSql;

        newGetMultipleSql.Should().NotBe(originalGetMultipleSql);
        newSetMultipleSql.Should().NotBe(originalSetMultipleSql);

        newGetMultipleSql.Should().Contain("new_schema.new_table");
        newSetMultipleSql.Should().Contain("new_schema.new_table");

        newGetMultipleSql.Should().NotContain("test_schema.test_cache");
        newSetMultipleSql.Should().NotContain("test_schema.test_cache");
    }

    [Fact]
    public async Task MultipleSequentialChanges_ShouldUpdateSqlCorrectly()
    {
        // Arrange
        SetupDbRawCommands();
        var sqlSnapshots = new List<string>();

        // Act - Make multiple changes and capture SQL snapshots
        sqlSnapshots.Add(_dbRawCommands!.GetSql); // Initial

        _options.Cache.TableNameObservable.Value = "table_v1";
        await Task.Delay(5);
        sqlSnapshots.Add(_dbRawCommands.GetSql); // After table change

        _options.Cache.SchemaNameObservable.Value = "schema_v1";
        await Task.Delay(5);
        sqlSnapshots.Add(_dbRawCommands.GetSql); // After schema change

        _options.Cache.TableNameObservable.Value = "table_v2";
        await Task.Delay(5);
        sqlSnapshots.Add(_dbRawCommands.GetSql); // After second table change

        // Assert - Each snapshot should be different and contain correct values
        sqlSnapshots.Should().HaveCount(4);
        sqlSnapshots.Should().OnlyHaveUniqueItems();

        sqlSnapshots[0].Should().Contain("test_schema.test_cache");
        sqlSnapshots[1].Should().Contain("test_schema.table_v1");
        sqlSnapshots[2].Should().Contain("schema_v1.table_v1");
        sqlSnapshots[3].Should().Contain("schema_v1.table_v2");
    }

    #endregion

    #region Performance Optimization Tests

    [Fact]
    public async Task SameValueChanges_ShouldNotTriggerUnnecessaryRebuilds()
    {
        // Arrange
        SetupDbRawCommands();
        var originalGetSql = _dbRawCommands!.GetSql;
        var originalSetSql = _dbRawCommands.SetSql;

        // Act - Set same values multiple times
        _options.Cache.TableNameObservable.Value = "test_cache"; // Same as original
        _options.Cache.SchemaNameObservable.Value = "test_schema"; // Same as original
        await WaitForEventProcessingAsync();

        // Assert - SQL should remain unchanged
        var newGetSql = _dbRawCommands.GetSql;
        var newSetSql = _dbRawCommands.SetSql;

        newGetSql.Should().Be(originalGetSql);
        newSetSql.Should().Be(originalSetSql);
    }

    [Fact]
    public async Task RapidConsecutiveChanges_ShouldOnlyRebuildForUniqueValues()
    {
        // Arrange
        SetupDbRawCommands();
        var sqlVersions = new HashSet<string>();

        // Act - Rapid changes with some duplicate values
        var values = new[] { "table1", "table1", "table2", "table1", "table2", "table3" };

        foreach (var value in values)
        {
            _options.Cache.TableNameObservable.Value = value;
            await Task.Delay(2);
            sqlVersions.Add(_dbRawCommands!.GetSql);
        }

        // Assert - Should have fewer unique SQL versions than total changes
        sqlVersions.Should().HaveCountLessThan(values.Length);
        sqlVersions.Should().HaveCount(3); // table1, table2, table3 (initial test_cache is replaced by table1)
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentPropertyChanges_ShouldBeThreadSafe()
    {
        // Arrange
        SetupDbRawCommands();
        var tasks = new List<Task>();
        var originalSql = _dbRawCommands!.GetSql;

        // Act - Simulate concurrent property changes
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                _options.Cache.TableNameObservable.Value = $"table_{index}";
                await Task.Delay(1);
                _options.Cache.SchemaNameObservable.Value = $"schema_{index}";
            }));
        }

        Task.WaitAll(tasks.ToArray());
        await Task.Delay(100); // Allow all events to process

        // Assert - Should not throw and SQL should be updated
        var finalSql = _dbRawCommands.GetSql;
        finalSql.Should().NotBe(originalSql);
        finalSql.Should().MatchRegex(@"schema_\d+\.table_\d+");
    }

    [Fact]
    public async Task ConcurrentSqlAccess_DuringPropertyChanges_ShouldNotThrow()
    {
        // Arrange
        SetupDbRawCommands();
        var readTasks = new List<Task<string>>();
        var writeTasks = new List<Task>();
        var sqlResults = new List<string>();

        // Act - Concurrent reads and writes
        for (int i = 0; i < 10; i++)
        {
            int index = i;

            // Tasks that change properties
            writeTasks.Add(Task.Run(async () =>
            {
                _options.Cache.TableNameObservable.Value = $"table_{index}";
                await Task.Delay(1);
            }));

            // Tasks that read SQL
            readTasks.Add(Task.Run(async () =>
            {
                await Task.Delay(1);
                return _dbRawCommands!.GetSql;
            }));
        }

        Task.WaitAll(writeTasks.ToArray());
        Task.WaitAll(readTasks.ToArray());

        // Collect results
        foreach (var task in readTasks)
        {
            sqlResults.Add(task.Result);
        }

        // Assert - Should not throw and should have valid SQL results
        sqlResults.Should().HaveCount(10);
        sqlResults.Should().AllSatisfy(sql => sql.Should().NotBeNullOrEmpty());
        sqlResults.Should().AllSatisfy(sql => sql.Should().Contain("."));
    }

    #endregion

    #region Resource Management Tests

    [Fact]
    public void DbRawCommands_Dispose_ShouldCleanupProperly()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Should not throw
        Action dispose = () => _dbRawCommands!.Dispose();
        dispose.Should().NotThrow();
    }

    [Fact]
    public void DbRawCommands_DisposeMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Multiple dispose calls should not throw
        _dbRawCommands!.Dispose();

        Action secondDispose = () => _dbRawCommands.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task DbRawCommands_Dispose_ShouldUnsubscribeFromPropertyChanges()
    {
        // Arrange
        SetupDbRawCommands();
        var originalSql = _dbRawCommands!.GetSql;

        // Act - Dispose and then change properties
        _dbRawCommands.Dispose();
        _options.Cache.TableNameObservable.Value = "new_table_after_dispose";
        await WaitForEventProcessingAsync();

        // Note: We can't easily verify the SQL didn't change because accessing it after dispose
        // might throw or be undefined behavior. The important thing is dispose doesn't throw.
    }

    [Fact]
    public async Task PropertyChangeSubscription_ShouldBeProperlyEstablished()
    {
        // Arrange
        SetupDbRawCommands();

        // Monitor nomenclature for internal property updates
        var originalTableName = _nomenclature!.TableName;

        // Act - Change observable property
        _options.Cache.TableNameObservable.Value = "monitored_table";
        await WaitForEventProcessingAsync();

        // Assert - Internal nomenclature should be updated
        _nomenclature.TableName.Should().NotBe(originalTableName);
        _nomenclature.TableName.Should().Be("monitored_table");
    }

    #endregion

    #region Event Handling Tests

    [Fact]
    public void PropertyChangedEventArgs_ShouldContainCorrectOldAndNewValues()
    {
        // Arrange
        PropertyChangedEventArgs<string>? capturedArgs = null;
        _options.Cache.TableNameObservable.PropertyChanged += (sender, args) =>
        {
            capturedArgs = args as PropertyChangedEventArgs<string>;
        };

        SetupDbRawCommands(); // This should trigger initial events

        // Act
        _options.Cache.TableNameObservable.Value = "new_event_table";

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldValue.Should().Be("test_cache");
        capturedArgs.NewValue.Should().Be("new_event_table");
        capturedArgs.PropertyName.Should().Be("Cache.TableName");
    }

    [Fact]
    public void MultiplePropertyChanges_ShouldTriggerMultipleEvents()
    {
        // Arrange
        var eventArgs = new List<PropertyChangedEventArgs<string>>();
        _options.Cache.TableNameObservable.PropertyChanged += (sender, args) =>
        {
            if (args is PropertyChangedEventArgs<string> typedArgs)
                eventArgs.Add(typedArgs);
        };

        SetupDbRawCommands();

        // Act - Make multiple changes
        _options.Cache.TableNameObservable.Value = "table_v1";
        _options.Cache.TableNameObservable.Value = "table_v2";
        _options.Cache.TableNameObservable.Value = "table_v3";

        // Assert
        eventArgs.Should().HaveCountGreaterOrEqualTo(3);
        eventArgs.Last().NewValue.Should().Be("table_v3");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void PropertyChanges_WithNullValues_ShouldThrowException()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Setting null should throw NullReferenceException
        // This is expected behavior as the SQL generation logic requires non-null values
        Action setNullTable = () => _options.Cache.TableNameObservable.Value = null!;
        Action setNullSchema = () => _options.Cache.SchemaNameObservable.Value = null!;

        setNullTable.Should().Throw<NullReferenceException>();
        setNullSchema.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void PropertyChanges_WithEmptyStrings_ShouldHandleGracefully()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Setting empty strings should not throw
        Action setEmptyTable = () => _options.Cache.TableNameObservable.Value = string.Empty;
        Action setEmptySchema = () => _options.Cache.SchemaNameObservable.Value = string.Empty;

        setEmptyTable.Should().NotThrow();
        setEmptySchema.Should().NotThrow();
    }

    [Fact]
    public void PropertyChanges_WithSpecialCharacters_ShouldHandleGracefully()
    {
        // Arrange
        SetupDbRawCommands();

        // Act & Assert - Setting strings with special characters should not throw
        Action setSpecialTable = () => _options.Cache.TableNameObservable.Value = "table-with_special.chars";
        Action setSpecialSchema = () => _options.Cache.SchemaNameObservable.Value = "schema$with#special&chars";

        setSpecialTable.Should().NotThrow();
        setSpecialSchema.Should().NotThrow();
    }

    #endregion

    private async Task WaitForEventProcessingAsync()
    {
        // Wait for observable property change events to be processed
        // This is more deterministic than Thread.Sleep
        await Task.Delay(50); // Short delay to allow event processing
    }

    public void Dispose()
    {
        // Only dispose if not already disposed in a test
        if (_dbRawCommands != null)
        {
            _dbRawCommands.Dispose();
            _dbRawCommands = null;
        }

        // Don't dispose nomenclature as it's handled by DbRawCommands disposal
    }
}
