using GlacialCache.PostgreSQL.Abstractions;

namespace GlacialCache.PostgreSQL.Tests.UnitTests.ManagerElection;

public class ManagerElectionEventsTests
{
    [Fact]
    public void ManagerElectedEventArgs_ShouldHaveCorrectProperties()
    {
        // Arrange
        var electedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var instanceId = "test-instance-123";

        // Act
        var args = new ManagerElectedEventArgs(electedAt, instanceId);

        // Assert
        args.ElectedAt.Should().Be(electedAt);
        args.InstanceId.Should().Be(instanceId);
    }

    [Fact]
    public void ManagerLostEventArgs_ShouldHaveCorrectProperties()
    {
        // Arrange
        var lostAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var instanceId = "test-instance-123";
        var reason = "Voluntary yield";

        // Act
        var args = new ManagerLostEventArgs(lostAt, instanceId, reason);

        // Assert
        args.LostAt.Should().Be(lostAt);
        args.InstanceId.Should().Be(instanceId);
        args.Reason.Should().Be(reason);
    }

    [Fact]
    public void ManagerLostEventArgs_ShouldHandleNullReason()
    {
        // Arrange
        var lostAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var instanceId = "test-instance-123";

        // Act
        var args = new ManagerLostEventArgs(lostAt, instanceId, null);

        // Assert
        args.LostAt.Should().Be(lostAt);
        args.InstanceId.Should().Be(instanceId);
        args.Reason.Should().BeNull();
    }

    [Fact]
    public void ManagerLostEventArgs_ShouldHandleEmptyReason()
    {
        // Arrange
        var lostAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var instanceId = "test-instance-123";
        var reason = "";

        // Act
        var args = new ManagerLostEventArgs(lostAt, instanceId, reason);

        // Assert
        args.LostAt.Should().Be(lostAt);
        args.InstanceId.Should().Be(instanceId);
        args.Reason.Should().Be(reason);
    }
}
