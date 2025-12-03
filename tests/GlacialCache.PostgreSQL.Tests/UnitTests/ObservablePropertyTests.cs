using Microsoft.Extensions.Logging;
using Moq;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.Logging;
using System.ComponentModel;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

/// <summary>
/// Comprehensive unit tests for ObservableProperty<T> functionality.
/// Tests core functionality, implicit operators, equality, thread safety, and logging integration.
/// </summary>
public class ObservablePropertyTests
{
    private readonly Mock<ILogger> _mockLogger;

    public ObservablePropertyTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    #region Basic Functionality Tests

    [Fact]
    public void Value_GetAndSet_ShouldWorkCorrectly()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        const string testValue = "test_value";

        // Act
        property.Value = testValue;

        // Assert
        property.Value.Should().Be(testValue);
    }

    [Fact]
    public void Value_SetSameValue_ShouldNotFirePropertyChanged()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        var eventFired = false;
        property.PropertyChanged += (_, _) => eventFired = true;

        // Act
        property.Value = "test";
        eventFired = false; // Reset flag
        property.Value = "test"; // Set same value

        // Assert
        eventFired.Should().BeFalse();
    }

    [Fact]
    public void Value_SetDifferentValue_ShouldFirePropertyChanged()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        var eventFired = false;
        property.PropertyChanged += (_, _) => eventFired = true;

        // Act
        property.Value = "initial";
        eventFired = false; // Reset flag
        property.Value = "changed"; // Set different value

        // Assert
        eventFired.Should().BeTrue();
    }

    [Fact]
    public void PropertyChanged_ShouldProvideCorrectOldAndNewValues()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        PropertyChangedEventArgs<string>? capturedArgs = null;

        property.PropertyChanged += (sender, args) =>
        {
            capturedArgs = args as PropertyChangedEventArgs<string>;
        };

        // Act
        property.Value = "initial";
        capturedArgs = null; // Reset
        property.Value = "changed";

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldValue.Should().Be("initial");
        capturedArgs.NewValue.Should().Be("changed");
        capturedArgs.PropertyName.Should().Be("TestProperty");
    }

    [Fact]
    public void PropertyChanged_WithDefaultValue_ShouldUseDefaultAsOldValue()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        PropertyChangedEventArgs<string>? capturedArgs = null;

        property.PropertyChanged += (sender, args) =>
        {
            capturedArgs = args as PropertyChangedEventArgs<string>;
        };

        // Act
        property.Value = "first_value";

        // Assert
        capturedArgs.Should().NotBeNull();
        capturedArgs!.OldValue.Should().BeNull(); // Default for string
        capturedArgs.NewValue.Should().Be("first_value");
    }

    #endregion

    #region Implicit Operators Tests

    [Fact]
    public void ImplicitOperator_ObservablePropertyToValue_ShouldWork()
    {
        // Arrange
        var property = new ObservableProperty<int> { Value = 42 };

        // Act
        int value = property; // Implicit conversion

        // Assert
        value.Should().Be(42);
    }

    [Fact]
    public void ImplicitOperator_ValueToObservableProperty_ShouldWork()
    {
        // Act
        ObservableProperty<int> property = 42; // Implicit conversion

        // Assert
        property.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitOperator_WithComplexType_ShouldWork()
    {
        // Arrange
        var testObject = new { Name = "Test", Value = 123 };
        var property = new ObservableProperty<object> { Value = testObject };

        // Act
        object value = property; // Implicit conversion

        // Assert
        value.Should().Be(testObject);
    }

    #endregion

    #region Equality and Comparison Tests

    [Fact]
    public void Equals_WithSameObservableProperty_ShouldReturnTrue()
    {
        // Arrange
        var property1 = new ObservableProperty<string> { Value = "test" };
        var property2 = new ObservableProperty<string> { Value = "test" };

        // Act & Assert
        property1.Equals(property2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentObservableProperty_ShouldReturnFalse()
    {
        // Arrange
        var property1 = new ObservableProperty<string> { Value = "test1" };
        var property2 = new ObservableProperty<string> { Value = "test2" };

        // Act & Assert
        property1.Equals(property2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithDirectValue_ShouldReturnTrue()
    {
        // Arrange
        var property = new ObservableProperty<string> { Value = "test" };

        // Act & Assert
        property.Equals("test").Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentDirectValue_ShouldReturnFalse()
    {
        // Arrange
        var property = new ObservableProperty<string> { Value = "test1" };

        // Act & Assert
        property.Equals("test2").Should().BeFalse();
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        // Arrange
        var property = new ObservableProperty<string> { Value = "test" };

        // Act & Assert
        property.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithNullValue_ShouldHandleCorrectly()
    {
        // Arrange
        var property1 = new ObservableProperty<string?> { Value = null };
        var property2 = new ObservableProperty<string?> { Value = null };
        var property3 = new ObservableProperty<string?> { Value = "test" };

        // Act & Assert
        property1.Equals(property2).Should().BeTrue(); // Both have null values
        property1.Equals(property3).Should().BeFalse(); // One null, one not null
        property1.Equals("test").Should().BeFalse(); // null value vs non-null string
        property1.Equals(null).Should().BeFalse(); // Object comparison with null should return false (standard behavior)
    }

    [Fact]
    public void GetHashCode_WithSameValues_ShouldBeSame()
    {
        // Arrange
        var property1 = new ObservableProperty<string> { Value = "test" };
        var property2 = new ObservableProperty<string> { Value = "test" };

        // Act & Assert
        property1.GetHashCode().Should().Be(property2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WithNullValue_ShouldReturnZero()
    {
        // Arrange
        var property = new ObservableProperty<string?> { Value = null };

        // Act & Assert
        property.GetHashCode().Should().Be(0);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WithStringValue_ShouldReturnValue()
    {
        // Arrange
        var property = new ObservableProperty<string> { Value = "test_string" };

        // Act & Assert
        property.ToString().Should().Be("test_string");
    }

    [Fact]
    public void ToString_WithIntValue_ShouldReturnStringRepresentation()
    {
        // Arrange
        var property = new ObservableProperty<int> { Value = 42 };

        // Act & Assert
        property.ToString().Should().Be("42");
    }

    [Fact]
    public void ToString_WithNullValue_ShouldReturnEmptyString()
    {
        // Arrange
        var property = new ObservableProperty<string?> { Value = null };

        // Act & Assert
        property.ToString().Should().Be(string.Empty);
    }

    #endregion

    #region Logging Integration Tests

    [Fact]
    public void Value_ChangeWithLogger_ShouldLogPropertyChange()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty", _mockLogger.Object);

        // Act
        property.Value = "initial";
        property.Value = "changed";

        // Assert
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Configuration property TestProperty changed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }

    [Fact]
    public void Value_SameValueWithLogger_ShouldNotLog()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty", _mockLogger.Object);
        property.Value = "test"; // Initial set

        _mockLogger.Reset(); // Clear previous calls

        // Act
        property.Value = "test"; // Same value

        // Assert
        _mockLogger.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void PropertyChanged_ExceptionInHandler_ShouldLogError()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty", _mockLogger.Object);
        property.PropertyChanged += (_, _) => throw new InvalidOperationException("Test exception");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => property.Value = "test");
        exception.Message.Should().Be("Test exception");

        // Verify error was logged
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error in observable property TestProperty")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithoutLogger_ShouldNotThrow()
    {
        // Act & Assert
        var createAction = () => new ObservableProperty<string>("TestProperty");
        createAction.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithEmptyPropertyName_ShouldNotThrow()
    {
        // Act & Assert
        var createAction = () => new ObservableProperty<string>("", _mockLogger.Object);
        createAction.Should().NotThrow();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void Value_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var property = new ObservableProperty<int>("TestProperty");
        var tasks = new List<Task>();
        var results = new List<int>();
        var lockObject = new object();

        // Act - Simulate concurrent read/write operations
        for (int i = 0; i < 50; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() =>
            {
                property.Value = value;
                Thread.Sleep(1); // Small delay to increase chance of race conditions
                var readValue = property.Value;
                lock (lockObject)
                {
                    results.Add(readValue);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Should not throw and should have collected all results
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(result => result.Should().BeInRange(0, 49));
    }

    [Fact]
    public void PropertyChanged_ConcurrentEventHandlers_ShouldBeThreadSafe()
    {
        // Arrange
        var property = new ObservableProperty<string>("TestProperty");
        var eventCount = 0;
        var lockObject = new object();

        // Register multiple event handlers
        for (int i = 0; i < 10; i++)
        {
            property.PropertyChanged += (_, _) =>
            {
                lock (lockObject)
                {
                    eventCount++;
                }
            };
        }

        var tasks = new List<Task>();

        // Act - Trigger property changes concurrently
        for (int i = 0; i < 10; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() => property.Value = $"value_{value}"));
        }

        Task.WaitAll(tasks.ToArray());
        Thread.Sleep(100); // Allow events to complete

        // Assert - Should have fired events for all changes
        eventCount.Should().BeGreaterOrEqualTo(90); // 10 handlers × 9+ unique values (first change from default)
    }

    #endregion

    #region Generic Type Tests

    [Fact]
    public void ObservableProperty_WithIntType_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var property = new ObservableProperty<int> { Value = 42 };

        // Assert
        property.Value.Should().Be(42);
        ((int)property).Should().Be(42);
    }

    [Fact]
    public void ObservableProperty_WithBoolType_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var property = new ObservableProperty<bool> { Value = true };

        // Assert
        property.Value.Should().BeTrue();
        ((bool)property).Should().BeTrue();
    }

    [Fact]
    public void ObservableProperty_WithCustomType_ShouldWorkCorrectly()
    {
        // Arrange
        var customObject = new TestCustomType { Name = "Test", Value = 123 };
        var property = new ObservableProperty<TestCustomType> { Value = customObject };

        // Act & Assert
        property.Value.Should().Be(customObject);
        property.Value.Name.Should().Be("Test");
        property.Value.Value.Should().Be(123);
    }

    private class TestCustomType
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    #endregion
}
