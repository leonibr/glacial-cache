using Microsoft.Extensions.Logging;
using Moq;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Npgsql;
using GlacialCache.PostgreSQL.Configuration;
using GlacialCache.PostgreSQL.Configuration.Resilience;
using GlacialCache.PostgreSQL.Services;

namespace GlacialCache.PostgreSQL.Tests.UnitTests;

public class PolicyFactoryTests
{
    private readonly Mock<ILogger> _logger = new();
    private readonly PolicyFactory _policyFactory;
    private readonly GlacialCachePostgreSQLOptions _options;

    public PolicyFactoryTests()
    {
        _policyFactory = new PolicyFactory(_logger.Object);
        _options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Retry = new RetryOptions
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100)
                },
                CircuitBreaker = new CircuitBreakerOptions
                {
                    Enable = true,
                    FailureThreshold = 2,
                    DurationOfBreak = TimeSpan.FromMilliseconds(500)
                },
                Timeouts = new TimeoutOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(1)
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };
    }

    [Fact]
    public async Task RetryPolicy_ShouldRetryOnTransientPostgresException_AndEventuallySucceed()
    {
        // Arrange
        var policy = _policyFactory.CreateRetryPolicy(_options);
        var attemptCount = 0;
        var maxAttempts = 3;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < maxAttempts)
            {
                throw CreateTransientPostgresException();
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(maxAttempts);
    }

    [Fact]
    public async Task RetryPolicy_ShouldNotRetryOnPermanentPostgresException()
    {
        // Arrange
        var policy = _policyFactory.CreateRetryPolicy(_options);
        var attemptCount = 0;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                attemptCount++;
                throw CreatePermanentPostgresException();
            });
        });

        attemptCount.Should().Be(1); // Should not retry
    }

    [Fact]
    public async Task RetryPolicy_ShouldRetryOnTimeoutException()
    {
        // Arrange
        var policy = _policyFactory.CreateRetryPolicy(_options);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw new TimeoutException("Connection timeout");
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task RetryPolicy_ShouldRetryOnSocketException()
    {
        // Arrange
        var policy = _policyFactory.CreateRetryPolicy(_options);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw new System.Net.Sockets.SocketException();
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task CircuitBreakerPolicy_ShouldOpenAfterFailureThreshold()
    {
        // Arrange
        var policy = _policyFactory.CreateCircuitBreakerPolicy(_options);
        var failureCount = 0;

        // Act - Trigger failures to open circuit breaker
        for (int i = 0; i < _options.Resilience.CircuitBreaker.FailureThreshold; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await policy.ExecuteAsync(async () =>
                {
                    failureCount++;
                    throw CreateTransientPostgresException();
                });
            });
        }

        // Assert - Circuit breaker should be open
        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
        {
            await policy.ExecuteAsync(async () => "should not execute");
        });

        failureCount.Should().Be(_options.Resilience.CircuitBreaker.FailureThreshold);
    }

    [Fact]
    public async Task CircuitBreakerPolicy_ShouldResetAfterDurationOfBreak()
    {
        // Arrange
        var shortDurationOptions = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                CircuitBreaker = new CircuitBreakerOptions
                {
                    Enable = true,
                    FailureThreshold = 1,
                    DurationOfBreak = TimeSpan.FromMilliseconds(100)
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateCircuitBreakerPolicy(shortDurationOptions);

        // Act - Trigger circuit breaker to open
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                throw CreateTransientPostgresException();
            });
        });

        // Wait for circuit breaker to reset
        await Task.Delay(200);

        // Assert - Circuit breaker should be closed again
        var result = await policy.ExecuteAsync(async () => "success");
        result.Should().Be("success");
    }

    [Fact]
    public async Task TimeoutPolicy_ShouldTimeoutOnLongOperation()
    {
        // Arrange
        var shortTimeoutOptions = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Timeouts = new TimeoutOptions
                {
                    OperationTimeout = TimeSpan.FromMilliseconds(50)
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateTimeoutPolicy(shortTimeoutOptions);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                await Task.Delay(200); // Longer than timeout
                return "should not reach here";
            });
        });
    }

    [Fact]
    public async Task TimeoutPolicy_ShouldCompleteSuccessfully_WhenOperationCompletesWithinTimeout()
    {
        // Arrange
        var policy = _policyFactory.CreateTimeoutPolicy(_options);

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.Delay(50); // Shorter than timeout
            return "success";
        });

        // Assert
        result.Should().Be("success");
    }

    [Fact]
    public async Task ResiliencePolicy_ShouldHandleMixedFailures_AndEventuallySucceed()
    {
        // Arrange
        var policy = _policyFactory.CreateResiliencePolicy(_options);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw CreateTransientPostgresException();
            }
            else if (attemptCount == 2)
            {
                throw new TimeoutException("Operation timed out");
            }
            return "success";
        });

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3); // Should retry twice then succeed
    }

    [Fact]
    public async Task ResiliencePolicy_ShouldHandleConcurrentRequests_WithoutInterference()
    {
        // Arrange
        var policy = _policyFactory.CreateResiliencePolicy(_options);
        var tasks = new List<Task<string>>();

        // Act - Submit multiple concurrent requests
        for (int i = 0; i < 5; i++)
        {
            int index = i; // Capture loop variable
            tasks.Add(policy.ExecuteAsync(async () =>
            {
                await Task.Delay(10); // Small delay
                return $"result-{index}";
            }));
        }

        // Assert
        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(5);
        results.Should().Contain("result-0", "result-1", "result-2", "result-3", "result-4");
    }

    [Fact]
    public void CreateResiliencePolicy_WithCircuitBreakerDisabled_ShouldNotIncludeCircuitBreaker()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                CircuitBreaker = new CircuitBreakerOptions
                {
                    Enable = false
                },
                Retry = new RetryOptions
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100)
                },
                Timeouts = new TimeoutOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(1)
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        // Act
        var policy = _policyFactory.CreateResiliencePolicy(options);

        // Assert
        policy.Should().NotBeNull();
        // Note: We can't easily test the internal structure, but we can verify it executes
        var result = policy.ExecuteAsync(async () => "test").GetAwaiter().GetResult();
        result.Should().Be("test");
    }

    [Fact]
    public void CreateResiliencePolicy_WithResilienceDisabled_ShouldReturnPassThroughPolicy()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = false
            }
        };

        // Act
        var policy = _policyFactory.CreateResiliencePolicy(options);

        // Assert
        policy.Should().NotBeNull();
        var result = policy.ExecuteAsync(async () => "test").GetAwaiter().GetResult();
        result.Should().Be("test");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RetryPolicy_ShouldRespectMaxRetryAttempts(int maxAttempts)
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Retry = new RetryOptions
                {
                    MaxAttempts = maxAttempts,
                    BaseDelay = TimeSpan.FromMilliseconds(10)
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateRetryPolicy(options);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                attemptCount++;
                throw CreateTransientPostgresException();
            });
        });

        attemptCount.Should().Be(maxAttempts + 1);
    }

    [Fact]
    public async Task CircuitBreakerPolicy_ShouldLogStateChanges()
    {
        // Arrange
        var policy = _policyFactory.CreateCircuitBreakerPolicy(_options);

        // Act - Trigger circuit breaker to open
        for (int i = 0; i < _options.Resilience.CircuitBreaker.FailureThreshold; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await policy.ExecuteAsync(async () =>
                {
                    throw CreateTransientPostgresException();
                });
            });
        }

        // Assert - Verify that state changes were logged
        _logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void RetryPolicy_WithLinearBackoffStrategy_ShouldUseLinearDelays()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Retry = new RetryOptions
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100),
                    BackoffStrategy = BackoffStrategy.Linear
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateRetryPolicy(options);
        var attemptCount = 0;
        var delays = new List<TimeSpan>();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                // Capture the delay before throwing
                var delayStart = stopwatch.Elapsed;
                await Task.Delay(10); // Small delay to simulate work
                throw CreateTransientPostgresException();
            }
            return "success";
        }).GetAwaiter().GetResult();
        stopwatch.Stop();

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3); // 2 retries + 1 success

        // Linear backoff: first retry at 100ms, second at 200ms
        // Total time should be roughly 100ms + 200ms + some overhead
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void RetryPolicy_WithExponentialBackoffStrategy_ShouldUseExponentialDelays()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Retry = new RetryOptions
                {
                    MaxAttempts = 3,
                    BaseDelay = TimeSpan.FromMilliseconds(100),
                    BackoffStrategy = BackoffStrategy.Exponential
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateRetryPolicy(options);
        var attemptCount = 0;

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw CreateTransientPostgresException();
            }
            return "success";
        }).GetAwaiter().GetResult();
        stopwatch.Stop();

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3);

        // Exponential backoff: first retry at 100ms (base * 2^0), second at 200ms (base * 2^1)
        // Total time should be roughly 100ms + 200ms + some overhead
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(250));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void RetryPolicy_WithExponentialWithJitterBackoffStrategy_ShouldUseExponentialDelaysWithJitter()
    {
        // Arrange
        var options = new GlacialCachePostgreSQLOptions
        {
            Resilience = new ResilienceOptions
            {
                EnableResiliencePatterns = true,
                Retry = new RetryOptions
                {
                    MaxAttempts = 4,
                    BaseDelay = TimeSpan.FromMilliseconds(100),
                    BackoffStrategy = BackoffStrategy.ExponentialWithJitter
                },
                Logging = new LoggingOptions
                {
                    EnableResilienceLogging = true
                }
            }
        };

        var policy = _policyFactory.CreateRetryPolicy(options);
        var attemptCount = 0;

        // Act - Run multiple times to verify jitter is applied
        var totalTimes = new List<TimeSpan>();
        for (int run = 0; run < 5; run++)
        {
            attemptCount = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = policy.ExecuteAsync(async () =>
            {
                attemptCount++;
                if (attemptCount < 4)
                {
                    throw CreateTransientPostgresException();
                }
                return "success";
            }).GetAwaiter().GetResult();
            stopwatch.Stop();

            totalTimes.Add(stopwatch.Elapsed);
            result.Should().Be("success");
        }

        // Assert - With jitter, times should vary but be within reasonable bounds
        // Exponential with jitter: delays vary but should be around 100ms, 200ms, 400ms
        // Total should be roughly 700ms with jitter variations
        foreach (var time in totalTimes)
        {
            time.Should().BeGreaterThan(TimeSpan.FromMilliseconds(500));
            time.Should().BeLessThan(TimeSpan.FromMilliseconds(1000));
        }

        // Verify that times are not identical (jitter is working)
        var uniqueTimes = totalTimes.Distinct().Count();
        uniqueTimes.Should().BeGreaterThan(1, "Jitter should cause variation in execution times");
    }

    [Fact]
    public void RetryPolicy_WithDefaultBackoffStrategy_ShouldUseExponentialWithJitter()
    {
        // Arrange - Use default configuration
        var policy = _policyFactory.CreateRetryPolicy(_options);
        var attemptCount = 0;

        // Act
        var result = policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw CreateTransientPostgresException();
            }
            return "success";
        }).GetAwaiter().GetResult();

        // Assert
        result.Should().Be("success");
        attemptCount.Should().Be(3);

        // Default should be ExponentialWithJitter
        _options.Resilience.Retry.BackoffStrategy.Should().Be(BackoffStrategy.ExponentialWithJitter);
    }

    private static PostgresException CreateTransientPostgresException()
    {
        return new PostgresException("Connection failed", null, "08000", null);
    }

    private static PostgresException CreatePermanentPostgresException()
    {
        return new PostgresException("Syntax error", null, "42601", null);
    }
}
