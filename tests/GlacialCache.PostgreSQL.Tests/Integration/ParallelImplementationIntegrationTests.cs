using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Tests.Shared;

namespace GlacialCache.PostgreSQL.Tests.Integration;

public class ParallelImplementationIntegrationTests : UnitIntegrationTestBase
{
    [Fact]
    public void ServiceRegistration_RegistersIOptionsMonitorSystem()
    {
        ExecuteWithServiceProvider(serviceProvider =>
        {
            // Assert - IOptionsMonitor system components
            var optionsMonitor = serviceProvider.GetService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            Assert.NotNull(optionsMonitor);

            // Assert - Configuration options
            var options = serviceProvider.GetService<IOptions<GlacialCachePostgreSQLOptions>>();
            Assert.NotNull(options);
            Assert.Equal("integration_cache", options.Value.Cache.TableName);
            Assert.Equal("integration_schema", options.Value.Cache.SchemaName);
        }, options =>
        {
            options.Cache.TableName = "integration_cache";
            options.Cache.SchemaName = "integration_schema";
        });
    }

    [Fact]
    public void ObservableConfiguration_WorksWithNewSystem()
    {
        ExecuteWithServiceProvider(serviceProvider =>
        {
            // Act - Get IOptionsMonitor system
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var options = serviceProvider.GetRequiredService<IOptions<GlacialCachePostgreSQLOptions>>();

            // Initialize observable properties through SetLogger methods
            InitializeObservableProperties<ParallelImplementationIntegrationTests>(serviceProvider, options.Value);

            // Test observable system functionality
            var observableEventTriggered = false;
            var observableEventArgs = (PropertyChangedEventArgs<string>?)null;

            options.Value.Cache.TableNameObservable.PropertyChanged += (sender, args) =>
            {
                observableEventTriggered = true;
                observableEventArgs = args as PropertyChangedEventArgs<string>;
            };

            // Act - Change observable property
            options.Value.Cache.TableNameObservable.Value = "new_observable_table";

            // Assert - Observable system works
            Assert.NotNull(optionsMonitor);
            Assert.True(observableEventTriggered);
            Assert.NotNull(observableEventArgs);
            Assert.Equal("observable_cache", observableEventArgs.OldValue);
            Assert.Equal("new_observable_table", observableEventArgs.NewValue);
            Assert.Equal("Cache.TableName", observableEventArgs.PropertyName);

            // Assert - Observable property value changed
            Assert.Equal("new_observable_table", options.Value.Cache.TableNameObservable.Value);
        }, options =>
        {
            options.Cache.TableName = "observable_cache";
            options.Cache.SchemaName = "observable_schema";
        });
    }

    [Fact]
    public void BidirectionalSynchronization_WorksBetweenRegularAndObservableProperties()
    {
        ExecuteWithServiceProvider(serviceProvider =>
        {
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var options = serviceProvider.GetRequiredService<IOptions<GlacialCachePostgreSQLOptions>>();

            // Initialize observable properties through SetLogger methods
            InitializeObservableProperties<ParallelImplementationIntegrationTests>(serviceProvider, options.Value);

            // Act: Test that observable properties are properly initialized
            Assert.Equal(options.Value.Cache.TableName, options.Value.Cache.TableNameObservable.Value);
            Assert.Equal(options.Value.Cache.SchemaName, options.Value.Cache.SchemaNameObservable.Value);

            // Act 2: Change observable property directly
            options.Value.Cache.TableNameObservable.Value = "observable_changed";

            // Assert 2: Observable property should be updated
            Assert.Equal("observable_changed", options.Value.Cache.TableNameObservable.Value);
        }, options =>
        {
            options.Cache.TableName = "sync_test_cache";
        });
    }

    [Fact]
    public void ObservableProperties_HaveCorrectDefaultValues()
    {
        ExecuteWithServiceProvider(serviceProvider =>
        {
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var options = serviceProvider.GetRequiredService<IOptions<GlacialCachePostgreSQLOptions>>();

            // Initialize observable properties through SetLogger methods
            InitializeObservableProperties<ParallelImplementationIntegrationTests>(serviceProvider, options.Value);

            // Assert - Cache defaults
            Assert.Equal("glacial_cache", options.Value.Cache.TableNameObservable.Value);
            Assert.Equal("public", options.Value.Cache.SchemaNameObservable.Value);

            // Assert - Connection defaults (will be synced from regular property)
            Assert.Equal(options.Value.Connection.ConnectionString, options.Value.Connection.ConnectionStringObservable.Value);

            // Assert - Connection Pool defaults  
            Assert.Equal(5, options.Value.Connection.Pool.MinSizeObservable.Value);
            Assert.Equal(50, options.Value.Connection.Pool.MaxSizeObservable.Value);
            Assert.Equal(300, options.Value.Connection.Pool.IdleLifetimeSecondsObservable.Value);
            Assert.Equal(10, options.Value.Connection.Pool.PruningIntervalSecondsObservable.Value);
        });
    }

    [Fact]
    public void ObservableProperties_MaintainTypeInformation()
    {
        ExecuteWithServiceProvider(serviceProvider =>
        {
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GlacialCachePostgreSQLOptions>>();
            var options = serviceProvider.GetRequiredService<IOptions<GlacialCachePostgreSQLOptions>>();

            // Initialize observable properties through SetLogger methods
            InitializeObservableProperties<ParallelImplementationIntegrationTests>(serviceProvider, options.Value);

            // Act & Assert - String properties
            Assert.IsType<ObservableProperty<string>>(options.Value.Cache.TableNameObservable);
            Assert.IsType<ObservableProperty<string>>(options.Value.Cache.SchemaNameObservable);
            Assert.IsType<ObservableProperty<string>>(options.Value.Connection.ConnectionStringObservable);

            // Act & Assert - Integer properties
            Assert.IsType<ObservableProperty<int>>(options.Value.Connection.Pool.MinSizeObservable);
            Assert.IsType<ObservableProperty<int>>(options.Value.Connection.Pool.MaxSizeObservable);
            Assert.IsType<ObservableProperty<int>>(options.Value.Connection.Pool.IdleLifetimeSecondsObservable);
            Assert.IsType<ObservableProperty<int>>(options.Value.Connection.Pool.PruningIntervalSecondsObservable);

            // Act & Assert - Implicit conversions work
            string tableName = options.Value.Cache.TableNameObservable;
            int minSize = options.Value.Connection.Pool.MinSizeObservable;

            Assert.Equal("glacial_cache", tableName);
            Assert.Equal(5, minSize);
        });
    }
}
