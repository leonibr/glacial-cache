namespace GlacialCache.PostgreSQL.Abstractions;

/// <summary>Compatibility name for the provider-neutral serializer contract.</summary>
[Obsolete("Use GlacialCache.Abstractions.ICacheEntrySerializer. This compatibility interface will be removed in a future major version.")]
public interface ICacheEntrySerializer : global::GlacialCache.Abstractions.ICacheEntrySerializer;
