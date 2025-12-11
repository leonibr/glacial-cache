using Xunit.Abstractions;
using Npgsql;

namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// Base class for integration tests that require Docker
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;

    protected IntegrationTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary>
    /// Initializes the test environment
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        await InitializeTestAsync();
    }

    /// <summary>
    /// Cleans up the test environment
    /// </summary>
    public virtual async Task DisposeAsync()
    {

        await CleanupTestAsync();

    }

    /// <summary>
    /// Override this method to perform test-specific initialization
    /// </summary>
    protected abstract Task InitializeTestAsync();

    /// <summary>
    /// Override this method to perform test-specific cleanup
    /// </summary>
    protected abstract Task CleanupTestAsync();
}
