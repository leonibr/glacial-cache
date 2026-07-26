namespace GlacialCache.PostgreSQL.Serializers;
using Abstractions;

/// <summary>
/// MemoryPack-based implementation of ICacheEntrySerializer with string optimization.
/// Provides 22% performance improvement for string serialization by using direct UTF8 encoding.
/// </summary>
[Obsolete("Use GlacialCache.Abstractions.MemoryPackCacheEntrySerializer. This compatibility type will be removed in a future major version.")]
public class MemoryPackCacheEntrySerializer : global::GlacialCache.Abstractions.MemoryPackCacheEntrySerializer, ICacheEntrySerializer;
