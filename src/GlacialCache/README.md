# GlacialCache abstractions

Provider-neutral contracts and typed behavior used by GlacialCache providers. This package is the canonical home of `IGlacialCache`, `CacheEntry<T>`, `ICacheEntry`, `ICacheEntrySerializer`, the MemoryPack serializer, and the cache-entry factory.

`IGlacialCache` exposes the same byte, rich-entry, typed, and batch surface for every provider. Applications should import `GlacialCache.Abstractions` and can switch providers without changing cache-facing code.

The former PostgreSQL `IGlacialCache` name remains as an obsolete compatibility interface. .NET cannot forward a type while changing its namespace, so code importing `GlacialCache.PostgreSQL.Models.CacheEntry<T>` must change that import to `GlacialCache.Abstractions`; the type's public shape is otherwise unchanged.
