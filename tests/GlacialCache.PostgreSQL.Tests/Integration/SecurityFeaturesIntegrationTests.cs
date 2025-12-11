using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GlacialCache.PostgreSQL.Extensions;
using GlacialCache.PostgreSQL.Abstractions;
using GlacialCache.PostgreSQL.Tests.Shared;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Services;
using Xunit.Abstractions;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Integration tests for security configuration options.
/// Tests verify that security features work correctly in real database scenarios.
/// Note: Some security features are defined but not yet implemented - these tests
/// verify configuration handling and document expected behavior.
/// </summary>
public class SecurityFeaturesIntegrationTests : IntegrationTestBase
{
    private PostgreSqlContainer? _postgres;
    private IServiceProvider? _serviceProvider;

    public SecurityFeaturesIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override async Task InitializeTestAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartWithRetryAsync(Output);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to initialize PostgreSQL container: {ex.Message}");
            throw new Exception($"Docker/PostgreSQL not available: {ex.Message}", ex);
        }
    }

    protected override async Task CleanupTestAsync()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            try
            {
                // await (_cleanupService?.StopAsync(default) ?? Task.CompletedTask);
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing service provider: {ex.Message}");
            }
        }

        if (_postgres != null)
        {
            try
            {
                await _postgres.DisposeAsync();
                Output.WriteLine("✅ PostgreSQL container disposed");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"⚠️ Warning: Error disposing container: {ex.Message}");
                // Don't throw - cleanup failures shouldn't fail tests
            }
            finally
            {
                _postgres = null;
            }
        }
    }

    private async Task<IGlacialCache> SetupCacheAsync(Action<GlacialCachePostgreSQLOptions> configureOptions)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Explicitly register TimeProvider.System to ensure test isolation
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddGlacialCachePostgreSQL(options =>
        {
            options.Connection.ConnectionString = new NpgsqlConnectionStringBuilder(_postgres!.GetConnectionString()) { ApplicationName = GetType().Name }.ConnectionString;
            options.Infrastructure.EnableManagerElection = false;
            options.Infrastructure.CreateInfrastructure = true;
            options.Maintenance.EnableAutomaticCleanup = false;
            options.Maintenance.CleanupInterval = TimeSpan.FromMilliseconds(250);

            // Configure security options
            configureOptions(options);
        });

        _serviceProvider = services.BuildServiceProvider();
        var cache = _serviceProvider.GetRequiredService<IGlacialCache>();

        return cache;
    }

    [Fact]
    public async Task Security_ConfigurationCanBeSetAndRetrieved()
    {
        // Arrange & Act - Set up cache with security configuration
        var cache = await SetupCacheAsync(options =>
        {
            options.Security.Tokens.EncryptInMemory = true;
            options.Security.Tokens.TokenRefreshBuffer = TimeSpan.FromMinutes(10);
            options.Security.Audit.EnableAuditLogging = true;
            options.Security.Audit.LogCacheAccessPatterns = true;
        });

        // Assert - Cache should be created successfully with security options
        cache.ShouldNotBeNull();

        // Verify basic functionality still works
        await cache.SetEntryAsync("security-test-key", "security-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrievedEntry = await cache.GetEntryAsync<string>("security-test-key");
        retrievedEntry.ShouldNotBeNull();
        retrievedEntry.Value.ShouldBe("security-test-value");
    }

    [Fact]
    public async Task Security_TokenOptions_DefaultValues()
    {
        // Arrange & Act - Set up cache with default security options
        var cache = await SetupCacheAsync(options =>
        {
            // Don't set security options - should use defaults
        });

        // Assert - Cache should work with default values
        cache.ShouldNotBeNull();

        // Verify basic functionality
        await cache.SetEntryAsync("default-security-test", "value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var result = await cache.GetEntryAsync<string>("default-security-test");
        result.ShouldNotBeNull();
        result.Value.ShouldBe("value");
    }

    [Fact]
    public async Task Security_AuditOptions_DefaultValues()
    {
        // Arrange & Act - Set up cache with default audit options (disabled)
        var cache = await SetupCacheAsync(options =>
        {
            // Audit logging should be disabled by default
        });

        // Assert - Cache should work with audit disabled
        cache.ShouldNotBeNull();

        // Perform operations that would generate audit logs if enabled
        await cache.SetEntryAsync("audit-test-1", "value1");
        await cache.SetEntryAsync("audit-test-2", "value2");
        var entry1 = await cache.GetEntryAsync<string>("audit-test-1");
        var entry2 = await cache.GetEntryAsync<string>("audit-test-2");
        await cache.RemoveAsync("audit-test-1");

        // Verify operations completed successfully
        entry1.ShouldNotBeNull();
        entry1.Value.ShouldBe("value1");
        entry2.ShouldNotBeNull();
        entry2.Value.ShouldBe("value2");

        // Verify no audit logs were created (since feature is not implemented yet)
        // This test documents expected behavior when audit logging is implemented
    }

    [Fact]
    public async Task Security_TokenEncryptionInMemory_ConfigurationValidation()
    {
        // Arrange & Act - Test various EncryptInMemory settings
        var testCases = new[] { true, false };

        foreach (var encryptInMemory in testCases)
        {
            var cache = await SetupCacheAsync(options =>
            {
                options.Security.Tokens.EncryptInMemory = encryptInMemory;
            });

            // Assert - Configuration should be accepted
            cache.ShouldNotBeNull();

            // Verify basic functionality still works regardless of encryption setting
            var testKey = $"encryption-test-{encryptInMemory}";
            await cache.SetEntryAsync(testKey, "test-value", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var retrieved = await cache.GetEntryAsync<string>(testKey);
            retrieved.ShouldNotBeNull();
            retrieved.Value.ShouldBe("test-value");
        }
    }

    [Fact]
    public async Task Security_TokenRefreshBuffer_ConfigurationValidation()
    {
        // Arrange & Act - Test various TokenRefreshBuffer settings
        var testBuffers = new[]
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5), // default
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1)
        };

        foreach (var buffer in testBuffers)
        {
            var cache = await SetupCacheAsync(options =>
            {
                options.Security.Tokens.TokenRefreshBuffer = buffer;
            });

            // Assert - Configuration should be accepted
            cache.ShouldNotBeNull();

            // Verify basic functionality
            var testKey = $"buffer-test-{buffer.TotalMinutes}";
            await cache.SetEntryAsync(testKey, "buffer-test-value", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            var retrieved = await cache.GetEntryAsync<string>(testKey);
            retrieved.ShouldNotBeNull();
            retrieved.Value.ShouldBe("buffer-test-value");
        }
    }

    [Fact]
    public async Task Security_AuditLogging_ConfigurationValidation()
    {
        // Arrange & Act - Test various audit logging settings
        var testCases = new[]
        {
            (EnableAuditLogging: true, LogCacheAccessPatterns: true),
            (EnableAuditLogging: true, LogCacheAccessPatterns: false),
            (EnableAuditLogging: false, LogCacheAccessPatterns: true),
            (EnableAuditLogging: false, LogCacheAccessPatterns: false)
        };

        foreach (var (enableAudit, logPatterns) in testCases)
        {
            var cache = await SetupCacheAsync(options =>
            {
                options.Security.Audit.EnableAuditLogging = enableAudit;
                options.Security.Audit.LogCacheAccessPatterns = logPatterns;
            });

            // Assert - All configurations should be accepted
            cache.ShouldNotBeNull();

            // Perform operations that would be audited
            var testKey = $"audit-config-test-{enableAudit}-{logPatterns}";
            await cache.SetEntryAsync(testKey, "audit-test-value", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var retrieved = await cache.GetEntryAsync<string>(testKey);
            retrieved.ShouldNotBeNull();
            retrieved.Value.ShouldBe("audit-test-value");
        }
    }

    [Fact]
    public async Task Security_CombinedSecurityConfiguration()
    {
        // Arrange & Act - Set up cache with all security features enabled
        var cache = await SetupCacheAsync(options =>
        {
            // Enable all security features
            options.Security.Tokens.EncryptInMemory = true;
            options.Security.Tokens.TokenRefreshBuffer = TimeSpan.FromMinutes(10);
            options.Security.Audit.EnableAuditLogging = true;
            options.Security.Audit.LogCacheAccessPatterns = true;

            // Also enable connection string masking for comprehensive security
            options.Security.ConnectionString.MaskInLogs = true;
            options.Security.ConnectionString.SensitiveParameters = new[] { "Password", "Token", "Key", "Secret" };
        });

        // Assert - Combined configuration should work
        cache.ShouldNotBeNull();

        // Perform comprehensive test operations
        var operations = new[]
        {
            ("security-op-1", "value1"),
            ("security-op-2", "value2"),
            ("security-op-3", "value3")
        };

        foreach (var (key, value) in operations)
        {
            await cache.SetEntryAsync(key, value, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });
        }

        // Verify all operations succeeded
        foreach (var (key, expectedValue) in operations)
        {
            var retrieved = await cache.GetEntryAsync<string>(key);
            retrieved.ShouldNotBeNull();
            retrieved.Value.ShouldBe(expectedValue);
        }

        // Clean up
        foreach (var (key, _) in operations)
        {
            await cache.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task Security_ConfigurationPersistence_AcrossOperations()
    {
        // Arrange - Set up cache with specific security configuration
        var cache = await SetupCacheAsync(options =>
        {
            options.Security.Tokens.EncryptInMemory = true;
            options.Security.Tokens.TokenRefreshBuffer = TimeSpan.FromMinutes(15);
            options.Security.Audit.EnableAuditLogging = true;
            options.Security.Audit.LogCacheAccessPatterns = false;
        });

        // Act - Perform multiple operations to ensure configuration persists
        for (int i = 0; i < 10; i++)
        {
            var key = $"persistence-test-{i}";
            var value = $"persistence-value-{i}";

            await cache.SetEntryAsync(key, value, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var retrieved = await cache.GetEntryAsync<string>(key);
            retrieved.ShouldNotBeNull();
            retrieved.Value.ShouldBe(value);

            // Immediate cleanup to avoid conflicts
            await cache.RemoveAsync(key);
        }

        // Assert - All operations completed successfully with configuration intact
        cache.ShouldNotBeNull();
    }

    /// <summary>
    /// This test documents the expected behavior for token encryption in memory.
    /// Since this feature is not yet implemented, this test serves as documentation
    /// and will need to be updated when the feature is implemented.
    /// </summary>
    [Fact]
    public async Task Security_TokenEncryptionInMemory_DocumentationTest()
    {
        // This test documents expected behavior - implementation pending
        var cache = await SetupCacheAsync(options =>
        {
            options.Security.Tokens.EncryptInMemory = true;
        });

        // Currently, this should work normally
        // When implemented, tokens should be encrypted in memory
        await cache.SetEntryAsync("token-encryption-test", "sensitive-token-data", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrieved = await cache.GetEntryAsync<string>("token-encryption-test");
        retrieved.ShouldNotBeNull();
        retrieved.Value.ShouldBe("sensitive-token-data");

        // TODO: When implemented, add assertions to verify encryption in memory
        // - Verify tokens are encrypted when stored in memory
        // - Verify tokens are decrypted when retrieved
        // - Verify encryption keys are managed securely
    }

    /// <summary>
    /// This test documents the expected behavior for audit logging.
    /// Since this feature is not yet implemented, this test serves as documentation
    /// and will need to be updated when the feature is implemented.
    /// </summary>
    [Fact]
    public async Task Security_AuditLogging_DocumentationTest()
    {
        // This test documents expected behavior - implementation pending
        var cache = await SetupCacheAsync(options =>
        {
            options.Security.Audit.EnableAuditLogging = true;
            options.Security.Audit.LogCacheAccessPatterns = true;
        });

        // Perform operations that should be audited
        await cache.SetEntryAsync("audit-test-key", "audit-test-value", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        var retrieved = await cache.GetEntryAsync<string>("audit-test-key");
        retrieved.ShouldNotBeNull();

        await cache.RemoveAsync("audit-test-key");

        // Currently, no audit logs are created
        // When implemented, add assertions to verify:
        // - Audit logs contain operation details (SET, GET, REMOVE)
        // - Audit logs contain timestamps
        // - Audit logs contain key information (masked if sensitive)
        // - Access patterns are tracked and logged
    }
}
