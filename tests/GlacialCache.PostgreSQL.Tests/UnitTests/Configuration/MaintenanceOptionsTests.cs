using GlacialCache.PostgreSQL.Configuration.Maintenance;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.Configuration;

public class MaintenanceOptionsTests
{
    [Fact]
    public void MaintenanceOptions_DefaultValues_WorkCorrectly()
    {
        // Arrange & Act
        var options = new MaintenanceOptions();

        // Assert - New simplified options have correct defaults
        options.EnableAutomaticCleanup.Should().BeTrue();
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(30));
        options.MaxCleanupBatchSize.Should().Be(1000);
    }

    [Fact]
    public void MaintenanceOptions_CustomValues_WorkCorrectly()
    {
        // Arrange & Act
        var options = new MaintenanceOptions
        {
            EnableAutomaticCleanup = false,
            CleanupInterval = TimeSpan.FromMinutes(15),
            MaxCleanupBatchSize = 500
        };

        // Assert - Custom values are preserved
        options.EnableAutomaticCleanup.Should().BeFalse();
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(15));
        options.MaxCleanupBatchSize.Should().Be(500);
    }

    [Fact]
    public void MaintenanceOptions_CleanupInterval_Validation()
    {
        // Arrange
        var options = new MaintenanceOptions();

        // Test valid intervals
        options.CleanupInterval = TimeSpan.FromMinutes(1);
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(1));

        options.CleanupInterval = TimeSpan.FromHours(1);
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));

        // Test zero interval (should be allowed - service just won't clean as frequently)
        options.CleanupInterval = TimeSpan.Zero;
        options.CleanupInterval.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void MaintenanceOptions_MaxCleanupBatchSize_Validation()
    {
        // Arrange
        var options = new MaintenanceOptions();

        // Test valid batch sizes
        options.MaxCleanupBatchSize = 1;
        options.MaxCleanupBatchSize.Should().Be(1);

        options.MaxCleanupBatchSize = 10000; // Upper limit from Range attribute
        options.MaxCleanupBatchSize.Should().Be(10000);

        // Test default value
        var defaultOptions = new MaintenanceOptions();
        defaultOptions.MaxCleanupBatchSize.Should().Be(1000);
    }

    [Fact]
    public void MaintenanceOptions_EnableAutomaticCleanup_Toggle()
    {
        // Arrange
        var options = new MaintenanceOptions();

        // Test default (enabled)
        options.EnableAutomaticCleanup.Should().BeTrue();

        // Test disabling
        options.EnableAutomaticCleanup = false;
        options.EnableAutomaticCleanup.Should().BeFalse();

        // Test re-enabling
        options.EnableAutomaticCleanup = true;
        options.EnableAutomaticCleanup.Should().BeTrue();
    }

    [Fact]
    public void MaintenanceOptions_SupportsConfigurationBuilderPattern()
    {
        // Arrange - Simulate configuration builder usage
        var options = new MaintenanceOptions
        {
            EnableAutomaticCleanup = true,
            CleanupInterval = TimeSpan.FromMinutes(45),
            MaxCleanupBatchSize = 2000
        };

        // Act - This simulates how it would be used in configuration
        Action configure = () =>
        {
            options.EnableAutomaticCleanup = false;
            options.CleanupInterval = TimeSpan.FromMinutes(60);
            options.MaxCleanupBatchSize = 5000;
        };

        configure();

        // Assert - Configuration changes are applied correctly
        options.EnableAutomaticCleanup.Should().BeFalse();
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(60));
        options.MaxCleanupBatchSize.Should().Be(5000);
    }
}
