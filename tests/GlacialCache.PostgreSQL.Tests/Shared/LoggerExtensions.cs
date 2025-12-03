using Microsoft.Extensions.Logging;
using Moq;

namespace GlacialCache.PostgreSQL.Tests.Shared;

public static class LoggerExtensions
{
    public static void VerifyLog<T>(
        this Mock<ILogger<T>> loggerMock,
        LogLevel level,
        string? contains = null,
        Times? times = null)
    {
        times ??= Times.AtLeastOnce();

        loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => contains == null || v.ToString()!.Contains(contains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times.Value
        );
    }
}
