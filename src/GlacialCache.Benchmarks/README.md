# GlacialCache Benchmarks

Performance benchmarks for GlacialCache.PostgreSQL using BenchmarkDotNet, including **direct comparisons** with [Sloop](https://github.com/dev-hancock/Sloop) - another PostgreSQL-backed IDistributedCache implementation.

## 🏃‍♂️ Running the Benchmarks

### Prerequisites

- **.NET 9.0 SDK or later**
- **Docker** (for PostgreSQL Testcontainers)
- Sufficient system resources for running PostgreSQL containers

### Execute Benchmarks

**GlacialCache-only benchmarks:**

```bash
cd src/GlacialCache.Benchmarks
dotnet run -c Release
```

**Direct GlacialCache vs Sloop comparison:**

```bash
# First, add Sloop package reference (check latest version)
# dotnet add package Sloop

# Then uncomment Sloop code in GlacialCacheVsSloopBenchmarks.cs and run:
cd src/GlacialCache.Benchmarks
dotnet run -c Release --project . GlacialCacheVsSloopBenchmarks
```

## 📊 Benchmark Tests

The benchmarks test the following operations at different parallelism levels (1, 10, 50):

### Core Operations

- **SetAsync**: Sequential cache write operations
- **GetAsync**: Sequential cache read operations
- **SetAsync_Parallel**: Parallel cache write operations
- **GetAsync_Parallel**: Parallel cache read operations

### Direct Comparison Tests (GlacialCache vs Sloop)

The comparative benchmark (`GlacialCacheVsSloopBenchmarks`) is **ready to run** with identical test conditions for both implementations:

**Current Status:**

- ✅ **GlacialCache benchmarks**: Ready to run
- ⚠️ **Sloop benchmarks**: Commented out (requires manual setup)

**Setup for Sloop Comparison:**

1. Add Sloop package: `dotnet add package Sloop --version x.x.x`
2. Uncomment Sloop service configuration in `Setup()` method
3. Uncomment Sloop benchmark methods
4. Update the Sloop API calls to match their current version

**When enabled, both implementations will use:**

- **Shared PostgreSQL Container**: Same database instance for fair comparison
- **Identical Test Data**: Same keys, values, and expiration settings
- **Same Connection Pool**: Both libraries use optimized PostgreSQL connection settings
- **Separate Schemas**: `glacial.cache_entries` vs `sloop.cache_entries` to avoid conflicts

### Test Configuration

- **Database**: PostgreSQL 16 Alpine (via Testcontainers)
- **Data Size**: Random values between 100B and 1KB
- **Pre-population**: 1,000 cache entries for read benchmarks
- **Expiration**: 30-minute sliding + 1-hour absolute expiration
- **Connection Pooling**: Enabled with optimized settings

## 🔍 Direct Comparison with Sloop

These benchmarks provide **head-to-head comparison** with [Sloop](https://github.com/dev-hancock/Sloop) using identical test conditions. The comparative benchmark tests both libraries side-by-side for accurate performance analysis.

### Expected Sloop Baseline Results

Based on [Sloop's published benchmark results](https://github.com/dev-hancock/Sloop#-benchmark-results):

| Framework | Method            | Parallelism | Expected Range |
| --------- | ----------------- | ----------- | -------------- |
| Sloop     | SetAsync          | 1           | ~60 µs         |
| Sloop     | GetAsync          | 1           | ~73 µs         |
| Sloop     | SetAsync_Parallel | 10          | ~580 µs        |
| Sloop     | GetAsync_Parallel | 10          | ~840 µs        |
| Sloop     | SetAsync_Parallel | 50          | ~870 µs        |
| Sloop     | GetAsync_Parallel | 50          | ~2,030 µs      |

## 🛠️ Technical Details

### Architecture

- Uses **Testcontainers** for isolated PostgreSQL instances
- Implements **connection pooling** with optimized settings
- Includes **clock synchronization** for multi-instance safety
- Features **background cleanup** of expired entries

### Key Differences from Sloop

- **Clock Synchronization Service**: Prevents drift in distributed scenarios
- **Advanced Expiration Handling**: Combined sliding + absolute expiration
- **Optimized Connection Management**: Custom data source with tuned pool settings
- **Enhanced Error Handling**: Comprehensive logging and resilience patterns

### Hardware Considerations

Benchmark results will vary based on:

- CPU performance (PostgreSQL processing)
- Memory availability (connection pooling)
- Storage I/O (database operations)
- Network latency (container communication)

## 📈 Interpreting Results

- **Lower mean times** = better performance
- **Lower memory allocation** = better efficiency
- **Parallel scaling** should show reasonable scaling with parallelism
- **Variance** indicates consistency of performance

## 🚀 Performance Optimization Tips

Based on benchmark results, consider:

1. **Connection Pool Tuning**: Adjust `MinPoolSize` and `MaxPoolSize`
2. **Cleanup Frequency**: Increase `ExpiredItemsDeletionInterval` for write-heavy workloads
3. **Clock Sync**: Disable `EnableClockSynchronization` for single-instance deployments
4. **Expiration Strategy**: Use appropriate sliding vs absolute expiration policies

## 🔧 Customizing Benchmarks

To modify the benchmarks:

1. **Change parallelism levels**: Update `[Params(1, 10, 50)]`
2. **Adjust data sizes**: Modify `GenerateRandomValue()` method
3. **Add custom scenarios**: Create new `[Benchmark]` methods
4. **Configure database**: Update PostgreSQL container settings in `Setup()`

## 📝 Example Output

```
BenchmarkDotNet=v0.14.0, OS=Windows 11
Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET 9.0.0 (9.0.24.52809), X64 RyuJIT

| Method            | Parallelism | Mean       | Error     | StdDev    | Allocated |
|------------------ |------------ |-----------:|----------:|----------:|----------:|
| SetAsync          | 1           | 65.2 µs    | 1.2 µs    | 1.1 µs    | 6.1 KB    |
| GetAsync          | 1           | 78.5 µs    | 1.5 µs    | 1.4 µs    | 4.2 KB    |
| SetAsync_Parallel | 10          | 620.3 µs   | 12.1 µs   | 11.3 µs   | 61.5 KB   |
| GetAsync_Parallel | 10          | 890.7 µs   | 17.8 µs   | 16.6 µs   | 42.1 KB   |
```

_Note: Results shown are illustrative. Actual performance will vary based on hardware and system configuration._
