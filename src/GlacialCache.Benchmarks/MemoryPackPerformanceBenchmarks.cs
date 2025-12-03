using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MemoryPack;
using System.Text.Json;
using GlacialCache.PostgreSQL.Models;

namespace GlacialCache.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class MemoryPackPerformanceBenchmarks
{
    private readonly string _testString = "Hello, World! This is a test string for benchmarking serialization performance.";
    private readonly int _testInt = 42;
    private readonly double _testDouble = 3.14159;
    private readonly DateTime _testDateTime = DateTime.UtcNow;
    private readonly int[] _testArray = Enumerable.Range(1, 100).ToArray();
    private readonly List<string> _testList = Enumerable.Range(1, 50).Select(i => $"Item {i}").ToList();
    private readonly Dictionary<string, int> _testDictionary = Enumerable.Range(1, 25).ToDictionary(i => $"Key{i}", i => i);
    private readonly ComplexTestObject _testComplexObject;

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

    [Benchmark]
    public byte[] MemoryPack_Serialize_String()
    {
        return MemoryPackSerializer.Serialize(_testString);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_String()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testString);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_Int()
    {
        return MemoryPackSerializer.Serialize(_testInt);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_Int()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testInt);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_Double()
    {
        return MemoryPackSerializer.Serialize(_testDouble);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_Double()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testDouble);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_DateTime()
    {
        return MemoryPackSerializer.Serialize(_testDateTime);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_DateTime()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testDateTime);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_Array()
    {
        return MemoryPackSerializer.Serialize(_testArray);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_Array()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testArray);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_List()
    {
        return MemoryPackSerializer.Serialize(_testList);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_List()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testList);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_Dictionary()
    {
        return MemoryPackSerializer.Serialize(_testDictionary);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_Dictionary()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testDictionary);
    }

    [Benchmark]
    public byte[] MemoryPack_Serialize_ComplexObject()
    {
        return MemoryPackSerializer.Serialize(_testComplexObject);
    }

    [Benchmark]
    public byte[] SystemTextJson_Serialize_ComplexObject()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_testComplexObject);
    }

    [Benchmark]
    public string MemoryPack_Deserialize_String()
    {
        var bytes = MemoryPackSerializer.Serialize(_testString);
        return MemoryPackSerializer.Deserialize<string>(bytes)!;
    }

    [Benchmark]
    public string SystemTextJson_Deserialize_String()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testString);
        return JsonSerializer.Deserialize<string>(bytes)!;
    }

    [Benchmark]
    public int MemoryPack_Deserialize_Int()
    {
        var bytes = MemoryPackSerializer.Serialize(_testInt);
        return MemoryPackSerializer.Deserialize<int>(bytes);
    }

    [Benchmark]
    public int SystemTextJson_Deserialize_Int()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testInt);
        return JsonSerializer.Deserialize<int>(bytes);
    }

    [Benchmark]
    public double MemoryPack_Deserialize_Double()
    {
        var bytes = MemoryPackSerializer.Serialize(_testDouble);
        return MemoryPackSerializer.Deserialize<double>(bytes);
    }

    [Benchmark]
    public double SystemTextJson_Deserialize_Double()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testDouble);
        return JsonSerializer.Deserialize<double>(bytes);
    }

    [Benchmark]
    public DateTime MemoryPack_Deserialize_DateTime()
    {
        var bytes = MemoryPackSerializer.Serialize(_testDateTime);
        return MemoryPackSerializer.Deserialize<DateTime>(bytes);
    }

    [Benchmark]
    public DateTime SystemTextJson_Deserialize_DateTime()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testDateTime);
        return JsonSerializer.Deserialize<DateTime>(bytes);
    }

    [Benchmark]
    public int[] MemoryPack_Deserialize_Array()
    {
        var bytes = MemoryPackSerializer.Serialize(_testArray);
        return MemoryPackSerializer.Deserialize<int[]>(bytes)!;
    }

    [Benchmark]
    public int[] SystemTextJson_Deserialize_Array()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testArray);
        return JsonSerializer.Deserialize<int[]>(bytes)!;
    }

    [Benchmark]
    public List<string> MemoryPack_Deserialize_List()
    {
        var bytes = MemoryPackSerializer.Serialize(_testList);
        return MemoryPackSerializer.Deserialize<List<string>>(bytes)!;
    }

    [Benchmark]
    public List<string> SystemTextJson_Deserialize_List()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testList);
        return JsonSerializer.Deserialize<List<string>>(bytes)!;
    }

    [Benchmark]
    public Dictionary<string, int> MemoryPack_Deserialize_Dictionary()
    {
        var bytes = MemoryPackSerializer.Serialize(_testDictionary);
        return MemoryPackSerializer.Deserialize<Dictionary<string, int>>(bytes)!;
    }

    [Benchmark]
    public Dictionary<string, int> SystemTextJson_Deserialize_Dictionary()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testDictionary);
        return JsonSerializer.Deserialize<Dictionary<string, int>>(bytes)!;
    }

    [Benchmark]
    public ComplexTestObject MemoryPack_Deserialize_ComplexObject()
    {
        var bytes = MemoryPackSerializer.Serialize(_testComplexObject);
        return MemoryPackSerializer.Deserialize<ComplexTestObject>(bytes)!;
    }

    [Benchmark]
    public ComplexTestObject SystemTextJson_Deserialize_ComplexObject()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_testComplexObject);
        return JsonSerializer.Deserialize<ComplexTestObject>(bytes)!;
    }

    [Benchmark]
    public CacheEntry<string> MemoryPack_CacheEntry_Creation()
    {
        return new CacheEntry<string>()
        {
            Key = "test-key",
            Value = _testString
        };
    }

    [Benchmark]
    public CacheEntry<string> MemoryPack_CacheEntry_Serialization()
    {
        var entry = new CacheEntry<string>()
        {
            Key = "test-key",
            Value = _testString
        };
        _ = entry.SerializedData; // Force serialization
        return entry;
    }

    [Benchmark]
    public CacheEntry<string> MemoryPack_CacheEntry_FromSerializedData()
    {
        var bytes = MemoryPackSerializer.Serialize(_testString);
        return new CacheEntry<string>()
        {
            Key = "test-key",
            Value = _testString
        };
    }

    [Benchmark]
    public CacheEntry<string> MemoryPack_CacheEntry_CreateUnserialized()
    {
        return new CacheEntry<string>()
        {
            Key = "test-key",
            Value = _testString
        };
    }
}

[MemoryPackable]
public partial record ComplexTestObject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
