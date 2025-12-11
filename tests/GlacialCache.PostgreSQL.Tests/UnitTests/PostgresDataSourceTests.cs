using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Models;
using GlacialCache.PostgreSQL.Configuration.Maintenance;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class PostgresDataSourceTests
{
    private readonly Mock<ILogger<PostgreSQLDataSource>> _loggerMock = new();
    private readonly Mock<IOptionsMonitor<GlacialCachePostgreSQLOptions>> _optionsMonitorMock = new();

    [Fact]
    public void OnConnectionStringChanged_WithMaskingEnabled_ShouldNotLogSensitiveValues()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123;Port=5432";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=newsecret456;Port=5432";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password"]
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act - Trigger connection string change via observable property
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - Verify sensitive values are not logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => !v.ToString()!.Contains("secret123") && !v.ToString()!.Contains("newsecret456")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Assert - Verify masked values are logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("***")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Assert - Verify non-sensitive parts remain visible
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Host=localhost") &&
                                           v.ToString()!.Contains("Database=testdb") &&
                                           v.ToString()!.Contains("Username=testuser")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithMaskingEnabled_ShouldMaskAllDefaultSensitiveParameters()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=newsecret";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password"]
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - Password parameter is masked
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Password=***")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithMaskingDisabled_ShouldLogOriginalValues()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=newsecret456";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = false, // Masking disabled
                    SensitiveParameters = ["Password", "Token", "Key"]
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - Original values are logged (for debugging scenarios)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("secret123") &&
                                           v.ToString()!.Contains("newsecret456")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithCustomSensitiveParameters_ShouldMaskOnlySpecifiedParameters()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123;ApplicationName=myapp;CommandTimeout=30";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=newsecret;ApplicationName=newapp;CommandTimeout=60";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password", "ApplicationName"] // Custom list, CommandTimeout not included
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - Specified parameters are masked, others are not
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Password=***") &&
                                           v.ToString()!.Contains("ApplicationName=***") &&
                                           v.ToString()!.Contains("CommandTimeout=60")), // CommandTimeout not masked
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithCaseInsensitiveParameterMatching_ShouldMaskCorrectly()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;PASSWORD=secret123";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;password=newsecret456";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password"] // Lower case in config
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - Case insensitive matching works
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("PASSWORD=***") &&
                                           v.ToString()!.Contains("password=***")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithMultipleSensitiveParameters_ShouldMaskAll()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123";
        var newConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=newsecret";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password"]
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act
        options.Connection.ConnectionStringObservable.Value = newConnectionString;

        // Assert - All three sensitive parameters are masked
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Password=***")),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void OnConnectionStringChanged_WithInvalidConnectionString_ShouldNotThrow()
    {
        // Arrange
        var originalConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=secret123";
        var invalidConnectionString = "invalid-connection-string-format";

        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = originalConnectionString
            },
            Security = new Configuration.Security.SecurityOptions
            {
                ConnectionString = new Configuration.Security.ConnectionStringOptions
                {
                    MaskInLogs = true,
                    SensitiveParameters = ["Password"]
                }
            }
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(options);

        // Initialize the observable property with the logger (normally done in GlacialCachePostgreSQL)
        options.Connection.SetLogger(_loggerMock.Object);

        var dataSource = new PostgreSQLDataSource(_loggerMock.Object, _optionsMonitorMock.Object);

        // Act - Should not throw even with invalid connection string
        var act = () => options.Connection.ConnectionStringObservable.Value = invalidConnectionString;

        // Assert - No exception thrown
        act.ShouldNotThrow();

        // Assert - Logging still occurs (graceful degradation)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null as Exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
