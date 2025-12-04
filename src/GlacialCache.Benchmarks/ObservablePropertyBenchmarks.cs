using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using GlacialCache.PostgreSQL.Configuration;

namespace GlacialCache.Benchmarks;

/// <summary>
/// Benchmarks for ObservableProperty performance - essential property change operations only.
/// Tests property get/set operations and event handling overhead.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
public class ObservablePropertyBenchmarks
{
    private ObservableProperty<string> _observableProperty = null!;

    [GlobalSetup]
    public void Setup()
    {
        _observableProperty = new ObservableProperty<string>("TestProperty");
    }

    /// <summary>
    /// Baseline: Setting a property value
    /// </summary>
    [Benchmark(Baseline = true)]
    public void SetValue()
    {
        _observableProperty.Value = "TestValue";
    }

    /// <summary>
    /// Getting a property value
    /// </summary>
    [Benchmark]
    public void GetValue()
    {
        var value = _observableProperty.Value;
        // Consume value to prevent optimization
        if (value == null)
        {
            throw new InvalidOperationException("Unexpected null value");
        }
    }

    /// <summary>
    /// Setting the same value twice (should not raise event on second set)
    /// </summary>
    [Benchmark]
    public void SetSameValue()
    {
        _observableProperty.Value = "SameValue";
        _observableProperty.Value = "SameValue"; // Should not raise event
    }

    /// <summary>
    /// Implicit conversion to value type
    /// </summary>
    [Benchmark]
    public void ImplicitConversion_ToValue()
    {
        string value = _observableProperty;
        // Consume value to prevent optimization
        if (value == null)
        {
            throw new InvalidOperationException("Unexpected null value");
        }
    }

    /// <summary>
    /// Implicit conversion from value type
    /// </summary>
    [Benchmark]
    public void ImplicitConversion_FromValue()
    {
        ObservableProperty<string> property = "TestValue";
        // Consume property to prevent optimization
        if (property.Value == null)
        {
            throw new InvalidOperationException("Unexpected null value");
        }
    }

    /// <summary>
    /// Multiple property changes in a loop
    /// </summary>
    [Benchmark]
    public void MultiplePropertyChanges()
    {
        for (int i = 0; i < 100; i++)
        {
            _observableProperty.Value = $"Value_{i}";
        }
    }

    /// <summary>
    /// Property changes with event handler attached (measures event overhead)
    /// </summary>
    [Benchmark]
    public void PropertyWithEventHandler()
    {
        var property = new ObservableProperty<string>("BenchmarkProperty");
        var eventCount = 0;
        property.PropertyChanged += (sender, args) => eventCount++;

        for (int i = 0; i < 100; i++)
        {
            property.Value = $"Value_{i}";
        }

        // Consume eventCount to prevent optimization
        if (eventCount != 100)
        {
            throw new InvalidOperationException($"Expected 100 events, got {eventCount}");
        }
    }
}
