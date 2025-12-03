using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Models.CommandParameters;
using Npgsql;
using Xunit;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.Extensions;

/// <summary>
/// Unit tests for NpgsqlCommandExtensions to verify type-safe parameter handling.
/// </summary>
public class NpgsqlCommandExtensionsTests
{
    [Fact]
    public void AddParameters_SetEntryParameters_AddsAllParametersWithCorrectNames()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var parameters = new SetEntryParameters
        {
            Key = "testKey",
            Value = new byte[] { 1, 2, 3 },
            Now = DateTimeOffset.UtcNow,
            RelativeInterval = TimeSpan.FromMinutes(10),
            SlidingInterval = TimeSpan.FromMinutes(5),
            ValueType = "System.String"
        };

        // Act
        command.AddParameters(parameters);

        // Assert
        Assert.Equal(6, command.Parameters.Count);
        Assert.NotNull(command.Parameters["@Key"]);
        Assert.NotNull(command.Parameters["@Value"]);
        Assert.NotNull(command.Parameters["@Now"]);
        Assert.NotNull(command.Parameters["@RelativeInterval"]);
        Assert.NotNull(command.Parameters["@SlidingInterval"]);
        Assert.NotNull(command.Parameters["@ValueType"]);
        
        Assert.Equal("testKey", command.Parameters["@Key"].Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, command.Parameters["@Value"].Value);
        Assert.Equal(TimeSpan.FromMinutes(10), command.Parameters["@RelativeInterval"].Value);
        Assert.Equal(TimeSpan.FromMinutes(5), command.Parameters["@SlidingInterval"].Value);
        Assert.Equal("System.String", command.Parameters["@ValueType"].Value);
    }

    [Fact]
    public void AddParameters_SetEntryParameters_WithNullValues_AddsDbNull()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var parameters = new SetEntryParameters
        {
            Key = "testKey",
            Value = new byte[] { 1, 2, 3 },
            Now = DateTimeOffset.UtcNow,
            RelativeInterval = null,
            SlidingInterval = null,
            ValueType = null
        };

        // Act
        command.AddParameters(parameters);

        // Assert
        Assert.Equal(6, command.Parameters.Count);
        Assert.Equal(DBNull.Value, command.Parameters["@RelativeInterval"].Value);
        Assert.Equal(DBNull.Value, command.Parameters["@SlidingInterval"].Value);
        Assert.Equal(DBNull.Value, command.Parameters["@ValueType"].Value);
    }

    [Fact]
    public void AddParameters_GetEntryParameters_AddsCorrectParameters()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var now = DateTimeOffset.UtcNow;
        var parameters = new GetEntryParameters
        {
            Key = "testKey",
            Now = now
        };

        // Act
        command.AddParameters(parameters);

        // Assert
        Assert.Equal(2, command.Parameters.Count);
        Assert.NotNull(command.Parameters["@Key"]);
        Assert.NotNull(command.Parameters["@Now"]);
        Assert.Equal("testKey", command.Parameters["@Key"].Value);
        Assert.Equal(now, command.Parameters["@Now"].Value);
    }

    [Fact]
    public void AddParameters_RemoveEntryParameters_AddsKeyParameter()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var parameters = new RemoveEntryParameters
        {
            Key = "testKey"
        };

        // Act
        command.AddParameters(parameters);

        // Assert
        Assert.Single(command.Parameters);
        Assert.NotNull(command.Parameters["@Key"]);
        Assert.Equal("testKey", command.Parameters["@Key"].Value);
    }

    [Fact]
    public void AddParameters_RefreshEntryParameters_AddsCorrectParameters()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var now = DateTimeOffset.UtcNow;
        var parameters = new RefreshEntryParameters
        {
            Key = "testKey",
            Now = now
        };

        // Act
        command.AddParameters(parameters);

        // Assert
        Assert.Equal(2, command.Parameters.Count);
        Assert.NotNull(command.Parameters["@Key"]);
        Assert.NotNull(command.Parameters["@Now"]);
        Assert.Equal("testKey", command.Parameters["@Key"].Value);
        Assert.Equal(now, command.Parameters["@Now"].Value);
    }

    [Fact]
    public void AddParameters_SetEntryParameters_ParameterNamesUsePascalCase()
    {
        // Arrange
        using var connection = new NpgsqlConnection("Host=localhost;Database=test;Username=test;Password=test");
        using var command = new NpgsqlCommand("SELECT 1", connection);
        
        var parameters = new SetEntryParameters
        {
            Key = "testKey",
            Value = new byte[] { 1, 2, 3 },
            Now = DateTimeOffset.UtcNow,
            RelativeInterval = TimeSpan.FromMinutes(10),
            SlidingInterval = TimeSpan.FromMinutes(5),
            ValueType = null
        };

        // Act
        command.AddParameters(parameters);

        // Assert - Parameter names should be PascalCase with @ prefix
        // Note: Npgsql treats parameter names as case-insensitive, so we verify they exist
        // but we ensure the actual ParameterName property uses PascalCase convention
        Assert.True(command.Parameters.Contains("@Key"));
        Assert.True(command.Parameters.Contains("@Value"));
        Assert.True(command.Parameters.Contains("@Now"));
        Assert.True(command.Parameters.Contains("@RelativeInterval"));
        Assert.True(command.Parameters.Contains("@SlidingInterval"));
        Assert.True(command.Parameters.Contains("@ValueType"));
        
        // Verify the actual parameter names are stored with PascalCase
        Assert.Equal("@Key", command.Parameters[0].ParameterName);
        Assert.Equal("@Value", command.Parameters[1].ParameterName);
        Assert.Equal("@Now", command.Parameters[2].ParameterName);
        Assert.Equal("@RelativeInterval", command.Parameters[3].ParameterName);
        Assert.Equal("@SlidingInterval", command.Parameters[4].ParameterName);
        Assert.Equal("@ValueType", command.Parameters[5].ParameterName);
    }
}

