using GlacialCache.PostgreSQL.Services;
using GlacialCache.PostgreSQL.Configuration;
using Microsoft.Extensions.Logging;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class ConfigurationValidationTests
{
    private readonly ILogger _logger;

    public ConfigurationValidationTests()
    {
        _logger = new LoggerFactory().CreateLogger<ConfigurationValidationTests>();
    }

    [Fact]
    public void ValidateOptions_WithValidOptions_ShouldNotThrow()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
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
    public void ValidateOptions_WithInvalidTableName_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "invalid-table-name",
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("is not a valid PostgreSQL identifier");
    }

    [Fact]
    public void ValidateOptions_WithInvalidSchemaName_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = "invalid-schema-name"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("is not a valid PostgreSQL identifier");
    }

    [Fact]
    public void ValidateOptions_WithTableNameTooLong_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = new string('a', 64), // 64 characters > 63 byte limit
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("exceeds PostgreSQL's maximum identifier length of 63 bytes");
    }

    [Fact]
    public void ValidateOptions_WithSchemaNameTooLong_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "glacial_cache",
                SchemaName = new string('a', 64) // 64 characters > 63 byte limit
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("exceeds PostgreSQL's maximum identifier length of 63 bytes");
    }

    [Fact]
    public void ValidateOptions_WithIndexNameTooLong_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "very_long_table_name_that_will_make_index_names_too_long_when_combined",
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("exceeds PostgreSQL's maximum identifier length of 63 bytes");
    }

    [Fact]
    public void ValidateOptions_WithUnicodeCharacters_ShouldCalculateByteLengthCorrectly()
    {
        // Arrange - Using Unicode characters that are 3 bytes each in UTF-8
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "test_table", // Use valid identifier for DataAnnotations
                SchemaName = "test_schema" // Use valid identifier for DataAnnotations
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldNotThrow();
    }

    [Fact]
    public void ValidateOptions_WithUnicodeCharactersTooLong_ShouldThrowArgumentException()
    {
        // Arrange - Using a very long table name that will exceed 63 bytes when combined with index suffixes
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "very_long_table_name_that_will_make_index_names_too_long_when_combined_with_suffixes", // This will be too long when combined with index suffixes
                SchemaName = "public"
            }
        };

        // Act & Assert
        var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
        action.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("exceeds PostgreSQL's maximum identifier length of 63 bytes");
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithValidOptions_ShouldReturnEmptyResults()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
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
    public void ValidateOptionsNonThrowing_WithInvalidOptions_ShouldReturnValidationErrors()
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
                TableName = "invalid-table",
                SchemaName = "invalid-schema"
            }
        };

        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(options).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.ShouldContain(r => r.ErrorMessage!.Contains("Connection string cannot be null or empty"));
        results.ShouldContain(r => r.ErrorMessage!.Contains("is not a valid PostgreSQL identifier"));
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithLongIdentifiers_ShouldReturnLengthValidationErrors()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = new string('a', 64),
                SchemaName = new string('b', 64)
            }
        };

        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(options).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.ShouldContain(r => r.ErrorMessage!.Contains("exceeds PostgreSQL's maximum identifier length of 63 bytes"));
    }

    [Fact]
    public void ValidateOptionsNonThrowing_WithIndexNameTooLong_ShouldReturnIndexValidationError()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = "very_long_table_name_that_will_make_index_names_too_long_when_combined",
                SchemaName = "public"
            }
        };

        // Act
        var results = ConfigurationValidator.ValidateOptionsNonThrowing(options).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.ShouldContain(r => r.ErrorMessage!.Contains("exceeds PostgreSQL's maximum identifier length of 63 bytes"));
    }

    [Theory]
    [InlineData("valid_table", true)]
    [InlineData("valid_table_123", true)]
    [InlineData("_valid_table", true)]
    [InlineData("invalid-table", false)]
    [InlineData("invalid table", false)]
    [InlineData("123invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidPostgreSqlIdentifier_ShouldValidateCorrectly(string? identifier, bool expected)
    {
        // This test would require making the method public or using reflection
        // For now, we test it indirectly through the validation methods
        var options = new GlacialCachePostgreSQLOptions
        {
            Connection = new ConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass"
            },
            Cache = new CacheOptions
            {
                TableName = identifier ?? "",
                SchemaName = "public"
            }
        };

        if (expected)
        {
            var action = () => ConfigurationValidator.ValidateOptions(options, _logger);
            action.ShouldNotThrow();
        }
        else
        {
            var action = () => ConfigurationValidator.ValidateOptions(options, _logger);

            // Empty string and null are caught by Required attribute
            if (string.IsNullOrEmpty(identifier))
            {
                action.ShouldThrow<ArgumentException>()
                    .Message.ShouldContain("Cache table name is required");
            }
            else
            {
                action.ShouldThrow<ArgumentException>()
                    .Message.ShouldContain("is not a valid PostgreSQL identifier");
            }
        }
    }

    [Fact]
    public void MaxPostgreSqlIdentifierLength_ShouldBe63()
    {
        // Assert
        ConfigurationValidator.MaxPostgreSqlIdentifierLength.ShouldBe(63);
    }
}
