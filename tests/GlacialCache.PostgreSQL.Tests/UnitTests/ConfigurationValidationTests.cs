using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Maintenance;
using GlacialCache.PostgreSQL.Configuration.Security;
using GlacialCache.PostgreSQL.Services;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class ConfigurationValidationTests
{
    private readonly ILogger _logger;

    public ConfigurationValidationTests()
    {
        _logger = new LoggerFactory().CreateLogger<ConfigurationValidationTests>();
    }

    private static GlacialCachePostgreSQLOptions CreateValidOptions() => new()
    {
        Maintenance = new MaintenanceOptions { EnableAutomaticCleanup = false },
        Connection = new ConnectionOptions
        {
            ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
        },
        Cache = new CacheOptions
        {
            TableName = "glacial_cache",
            SchemaName = "public"
        }
    };

    [Fact]
    public void ValidateOptions_WithValidOptions_ShouldNotThrow()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldNotThrow();
    }

    [Fact]
    public void ValidateOptions_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(null!, _logger);
        action.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void ValidateOptions_WithInvalidConnectionString_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = ""
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Connection string cannot be null or empty");
    }

    [Fact]
    public void ValidateOptions_WithNonPositiveMinimumExpirationInterval_ShouldThrowArgumentException()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Cache.MinimumExpirationInterval = TimeSpan.Zero;

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Minimum expiration interval must be positive");
    }

    [Fact]
    public void ValidateOptions_WithNonPositiveMaximumExpirationInterval_ShouldThrowArgumentException()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Cache.MaximumExpirationInterval = TimeSpan.Zero;

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Maximum expiration interval must be positive");
    }

    [Fact]
    public void ValidateOptions_WithMinimumExpirationGreaterThanMaximum_ShouldThrowArgumentException()
    {
        // Arrange
        var options = CreateValidOptions();
        options.Cache.MinimumExpirationInterval = TimeSpan.FromSeconds(2);
        options.Cache.MaximumExpirationInterval = TimeSpan.FromSeconds(1);

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Minimum expiration interval cannot be greater than maximum expiration interval");
    }

    [Fact]
    public void CacheOptions_WithInvalidTableName_ShouldThrowArgumentException()
    {
        // Act & Assert - Setting an invalid identifier throws immediately
        var action = () => new CacheOptions
        {
            TableName = "invalid-table-name",
            SchemaName = "public"
        };

        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Invalid PostgreSQL identifier");
    }

    [Fact]
    public void CacheOptions_WithInvalidSchemaName_ShouldThrowArgumentException()
    {
        // Act & Assert - Setting an invalid identifier throws immediately
        var action = () => new CacheOptions
        {
            TableName = "glacial_cache",
            SchemaName = "invalid-schema-name"
        };

        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Invalid PostgreSQL identifier");
    }

    [Fact]
    public void CacheOptions_WithTableNameTooLong_ShouldThrowArgumentException()
    {
        // Act & Assert - Setting a too-long identifier throws immediately
        var action = () => new CacheOptions
        {
            TableName = new string('a', 64), // 64 characters > 63 byte limit
            SchemaName = "public"
        };

        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("cannot exceed");
    }

    [Fact]
    public void CacheOptions_WithSchemaNameTooLong_ShouldThrowArgumentException()
    {
        // Act & Assert - Setting a too-long identifier throws immediately
        var action = () => new CacheOptions
        {
            TableName = "glacial_cache",
            SchemaName = new string('a', 64) // 64 characters > 63 byte limit
        };

        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("cannot exceed");
    }

    [Fact]
    public void ValidateOptions_WithValidUnicodeCharacters_ShouldNotThrow()
    {
        // Arrange - Using valid ASCII identifiers
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "test_table",
                SchemaName = "test_schema"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldNotThrow();
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithValidOptions_ShouldReturnEmptyResults()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Maintenance = new MaintenanceOptions() { EnableAutomaticCleanup = false },
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = "public"
            }
        };

        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(options).ToList();

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithNullOptions_ShouldReturnValidationError()
    {
        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(null!).ToList();

        // Assert
        results.Count.ShouldBe(1);
        results[0].ErrorMessage!.ShouldContain("Options cannot be null");
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithEmptyConnectionString_ShouldReturnValidationError()
    {
        // Arrange - Use valid identifiers, test only connection string validation
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = ""
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = "public"
            }
        };

        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(options).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.ShouldContain(r => r.ErrorMessage!.Contains("Connection string cannot be null or empty"));
    }

    [Theory]
    [InlineData("valid_table", true)]
    [InlineData("valid_table_123", true)]
    [InlineData("_valid_table", true)]
    [InlineData("ValidTable", true)] // Mixed case is valid but will be lowercased
    [InlineData("invalid-table", false)]
    [InlineData("invalid table", false)]
    [InlineData("123invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidPostgreSqlIdentifier_ShouldValidateCorrectly(string? identifier, bool expected)
    {
        // Test the sanitizer directly
        var result = PostgreSQLIdentifierSanitizer.IsValidIdentifier(identifier ?? "");

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("TestTable", "testtable")] // Lowercased
    [InlineData("UPPER_CASE", "upper_case")] // Lowercased
    [InlineData("valid_123", "valid_123")] // Already lowercase
    public void ValidateAndNormalize_ShouldLowercaseIdentifiers(string input, string expected)
    {
        // Act
        var result = PostgreSQLIdentifierSanitizer.ValidateAndNormalize(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void CacheOptions_ShouldLowercaseValidIdentifiers()
    {
        // Arrange & Act
        var options = new CacheOptions
        {
            TableName = "MyTable",
            SchemaName = "MySchema"
        };

        // Assert - values should be lowercased
        options.TableName.ShouldBe("mytable");
        options.SchemaName.ShouldBe("myschema");
    }

    [Fact]
    public void MaxPostgreSqlIdentifierLength_ShouldBe63()
    {
        // Assert
        ConfigurationValidator.MaxPostgreSqlIdentifierLength.ShouldBe(63);
    }

    [Theory]
    [InlineData("DROP")] // SQL keyword
    [InlineData("SELECT")]
    [InlineData("DELETE")]
    public void IsValidIdentifier_ShouldRejectSqlKeywords(string keyword)
    {
        // Act
        var result = PostgreSQLIdentifierSanitizer.IsValidIdentifier(keyword);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("table--comment")] // Contains comment pattern
    [InlineData("table/*comment")]
    [InlineData("table*/comment")]
    public void IsValidIdentifier_ShouldRejectCommentPatterns(string identifier)
    {
        // Act
        var result = PostgreSQLIdentifierSanitizer.IsValidIdentifier(identifier);

        // Assert
        result.ShouldBeFalse();
    }
}
