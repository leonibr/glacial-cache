using MemoryPack;

namespace GlacialCache.PostgreSQL.Tests.Integration;

/// <summary>
/// Test data classes for serializer integration tests.
/// </summary>
[MemoryPackable]
public partial class TestDataObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Ratio { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string[] Items { get; set; } = Array.Empty<string>();
}

[MemoryPackable]
public partial class NestedObject
{
    public string Value { get; set; } = string.Empty;
    public int[] Numbers { get; set; } = Array.Empty<int>();
}
