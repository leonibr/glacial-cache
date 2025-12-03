namespace GlacialCache.PostgreSQL.Tests.Shared;

/// <summary>
/// A simple fake time provider for testing purposes.
/// </summary>
public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _currentTime;

    public FakeTimeProvider()
    {
        _currentTime = DateTimeOffset.UtcNow;
    }

    public FakeTimeProvider(DateTimeOffset initialTime)
    {
        _currentTime = initialTime;
    }

    public override DateTimeOffset GetUtcNow() => _currentTime;

    public void SetUtcNow(DateTimeOffset newTime)
    {
        _currentTime = newTime;
    }

    public void Advance(TimeSpan timeSpan)
    {
        _currentTime = _currentTime.Add(timeSpan);
    }
}
