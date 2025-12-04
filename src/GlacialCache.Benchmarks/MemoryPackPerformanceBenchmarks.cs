using BenchmarkDotNet.Attributes;
using MemoryPack;
using System.Text.Json;

namespace GlacialCache.Benchmarks;

/// <summary>
/// Benchmarks comparing MemoryPack vs System.Text.Json serialization performance.
/// Tests representative types: String (simple) and ComplexObject (complex with nested structures).
/// Serialize and deserialize operations are properly separated with pre-serialized data in setup.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MemoryPackPerformanceBenchmarks
{
    private readonly string _testString = "Hello, World! This is a test string for benchmarking serialization performance.";
    private readonly ComplexTestObject _testComplexObject;

    // Pre-serialized data for deserialize benchmarks (no serialization in hot path)
    private byte[] _memoryPackStringBytes = null!;
    private byte[] _systemTextJsonStringBytes = null!;
    private byte[] _memoryPackComplexObjectBytes = null!;
    private byte[] _systemTextJsonComplexObjectBytes = null!;

    public MemoryPackPerformanceBenchmarks()
    {
        _testComplexObject = new ComplexTestObject
        {
            Id = 42,
            Name = "Test Object",
            Tags = new[] { "tag1", "tag2", "tag3", "tag4", "tag5" },
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123,
                ["key3"] = 3.14,
                ["key4"] = true,
                ["key5"] = DateTime.UtcNow
            }
        };
    }

    [GlobalSetup]
    public void Setup()
    {
        // Pre-serialize data for deserialize benchmarks to avoid serialization overhead in hot path
        _memoryPackStringBytes = MemoryPackSerializer.Serialize(_testString);
        _systemTextJsonStringBytes = JsonSerializer.SerializeToUtf8Bytes(_testString);
        _memoryPackComplexObjectBytes = MemoryPackSerializer.Serialize(_testComplexObject);
        _systemTextJsonComplexObjectBytes = JsonSerializer.SerializeToUtf8Bytes(_testComplexObject);
    }

    #region Serialize Benchmarks

    /// <summary>
    /// Baseline: System.Text.Json serialization of string
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Serialize")]
    public byte[] SystemTextJson_Serialize_String()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testString);
    }

    /// <summary>
    /// MemoryPack serialization of string
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public byte[] MemoryPack_Serialize_String()
    {
        return MemoryPackSerializer.Serialize(_testString);
    }

    /// <summary>
    /// Baseline: System.Text.Json serialization of complex object
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Serialize")]
    public byte[] SystemTextJson_Serialize_ComplexObject()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testComplexObject);
    }

    /// <summary>
    /// MemoryPack serialization of complex object
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public byte[] MemoryPack_Serialize_ComplexObject()
    {
        return MemoryPackSerializer.Serialize(_testComplexObject);
    }

    #endregion

    #region Deserialize Benchmarks

    /// <summary>
    /// Baseline: System.Text.Json deserialization of string (pre-serialized in setup)
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize")]
    public string SystemTextJson_Deserialize_String()
    {
        return JsonSerializer.Deserialize<string>(_systemTextJsonStringBytes)!;
    }

    /// <summary>
    /// MemoryPack deserialization of string (pre-serialized in setup)
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public string MemoryPack_Deserialize_String()
    {
        return MemoryPackSerializer.Deserialize<string>(_memoryPackStringBytes)!;
    }

    /// <summary>
    /// Baseline: System.Text.Json deserialization of complex object (pre-serialized in setup)
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize")]
    public ComplexTestObject SystemTextJson_Deserialize_ComplexObject()
    {
        return JsonSerializer.Deserialize<ComplexTestObject>(_systemTextJsonComplexObjectBytes)!;
    }

    /// <summary>
    /// MemoryPack deserialization of complex object (pre-serialized in setup)
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public ComplexTestObject MemoryPack_Deserialize_ComplexObject()
    {
        return MemoryPackSerializer.Deserialize<ComplexTestObject>(_memoryPackComplexObjectBytes)!;
    }

    #endregion
}

/// <summary>
/// Complex test object with nested structures for realistic serialization benchmarks
/// </summary>
[MemoryPackable]
public partial record ComplexTestObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
