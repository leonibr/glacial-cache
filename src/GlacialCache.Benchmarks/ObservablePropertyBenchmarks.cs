using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GlacialCache.PostgreSQL.Configuration;

namespace GlacialCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ObservablePropertyBenchmarks
{
    private ObservableProperty<string> _observableProperty = null!;
    private ObservableProperty<string> _observablePropertyWithLogger = null!;
    private ILogger _logger = null!;

    [GlobalSetup]
    public void Setup()
    {
        _logger = NullLogger.Instance;
        _observableProperty = new ObservableProperty<string>("TestProperty");
        _observablePropertyWithLogger = new ObservableProperty<string>("TestProperty", _logger);
    }

    [Benchmark(Baseline = true)]
    public void SetValue_WithoutLogger()
    {
        _observableProperty.Value = "TestValue";
    }

    [Benchmark]
    public void SetValue_WithLogger()
    {
        _observablePropertyWithLogger.Value = "TestValue";
    }

    [Benchmark]
    public void GetValue_WithoutLogger()
    {
        var value = _observableProperty.Value;
    }

    [Benchmark]
    public void GetValue_WithLogger()
    {
        var value = _observablePropertyWithLogger.Value;
    }

    [Benchmark]
    public void SetSameValue_WithoutLogger()
    {
        _observableProperty.Value = "SameValue";
        _observableProperty.Value = "SameValue"; // Should not raise event
    }

    [Benchmark]
    public void SetSameValue_WithLogger()
    {
        _observablePropertyWithLogger.Value = "SameValue";
        _observablePropertyWithLogger.Value = "SameValue"; // Should not raise event
    }

    [Benchmark]
    public void ImplicitConversion_ToValue()
    {
        string value = _observableProperty;
    }

    [Benchmark]
    public void ImplicitConversion_FromValue()
    {
        ObservableProperty<string> property = "TestValue";
    }

    [Benchmark]
    public void MultiplePropertyChanges()
    {
        for (int i = 0; i < 100; i++)
        {
            _observableProperty.Value = $"Value_{i}";
        }
    }

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
    }
}
