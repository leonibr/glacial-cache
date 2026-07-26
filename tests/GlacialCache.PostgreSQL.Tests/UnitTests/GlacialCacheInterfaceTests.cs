using Microsoft.Extensions.Caching.Distributed;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class GlacialCacheInterfaceTests
{
    [Fact]
    public void IGlacialCache_IsDistributedCacheContract()
    {
        typeof(IDistributedCache).IsAssignableFrom(typeof(IGlacialCache)).ShouldBeTrue();
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void IGlacialCache_IsBufferDistributedCacheContract()
    {
        typeof(IBufferDistributedCache).IsAssignableFrom(typeof(IGlacialCache)).ShouldBeTrue();
    }
#endif
}
