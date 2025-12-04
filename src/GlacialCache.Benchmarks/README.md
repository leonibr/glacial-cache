# GlacialCache Benchmarks

Performance benchmarks for GlacialCache.PostgreSQL using BenchmarkDotNet.

## 🏃‍♂️ Running the Benchmarks

### Prerequisites

- **.NET 10.0 SDK or later**
- **Docker** (for PostgreSQL Testcontainers)
- Sufficient system resources for running PostgreSQL containers

### Execute Benchmarks

**Run all benchmarks:**

```bash
cd src/GlacialCache.Benchmarks
dotnet run -c Release
```

**Run verification test before benchmarks:**

```bash
cd src/GlacialCache.Benchmarks
dotnet run -c Release -- --test
```

**Run specific benchmark class:**

```bash
cd src/GlacialCache.Benchmarks
dotnet run -c Release -- --filter *GlacialCacheBatchBenchmarks*
```

**Alternative ways to run specific benchmarks:**

```bash
# Using filter with wildcard pattern
dotnet run -c Release -- --filter *GlacialCacheBatchBenchmarks*

# Using filter without wildcard (exact match)
dotnet run -c Release -- --filter GlacialCacheBatchBenchmarks

# Using benchmark number (shown in interactive menu)
dotnet run -c Release -- 1
```

## 📊 Benchmark Suite

The benchmark suite has been consolidated and optimized to focus on meaningful, high-impact benchmarks:

### 1. GlacialCacheBatchBenchmarks

**Purpose**: Compare batch operations (SetMultipleAsync/GetMultipleAsync) vs parallel individual operations.

**Tests**:

- **Individual Operations** (Baseline): Parallel SetAsync/GetAsync/RemoveAsync using Task.WhenAll
- **Batch Operations**: SetMultipleAsync/GetMultipleAsync/RemoveMultipleAsync/RefreshMultipleAsync using single connection with NpgsqlBatch

**Parameters**:

- Batch Size: 10, 50, 100, 500 (small, medium, large, very large)

**Key Insights**:

- Batch operations use a single connection from the pool with NpgsqlBatch
- Should show significant performance improvement over parallel individual operations for larger batch sizes
- Demonstrates the efficiency of batch operations for bulk cache updates

### 2. MemoryPackPerformanceBenchmarks

**Purpose**: Compare MemoryPack vs System.Text.Json serialization performance.

**Tests**:

- **Serialize**: String and ComplexObject serialization
- **Deserialize**: String and ComplexObject deserialization (pre-serialized in setup)

**Key Features**:

- Only tests representative types (String for simple, ComplexObject for complex scenarios)
- Properly separates serialize/deserialize operations (no serialization in deserialize hot path)
- System.Text.Json marked as baseline for comparison
- Pre-serialized data in GlobalSetup to avoid overhead

### 3. ObservablePropertyBenchmarks

**Purpose**: Measure ObservableProperty performance for configuration management.

**Tests**:

- Property get/set operations
- Implicit conversions
- Multiple property changes
- Event handler overhead

**Key Features**:

- Essential property change operations only
- Tests event handling overhead
- Useful for understanding configuration change performance

### 4. BatchOperationsTest (Verification Tool)

**Purpose**: Pre-benchmark verification test to ensure batch operations work correctly.

**Note**: This is a verification test, not a benchmark. Useful for CI/CD validation before running performance benchmarks.

## 🔍 Benchmark Design Principles

The benchmarks follow BenchmarkDotNet best practices:

1. **Proper Isolation**: Each benchmark tests one specific operation
2. **Pre-generated Data**: All test data generated in `[GlobalSetup]`, no random generation in hot path
3. **Result Consumption**: All results are consumed/validated to prevent JIT optimization
4. **Baseline Marking**: Appropriate benchmarks marked as baseline for comparison
5. **Parameterization**: Consistent use of `[Params]` for scalability testing
6. **Memory Diagnostics**: `[MemoryDiagnoser]` used where memory matters

## 🛠️ Technical Details

### Architecture

- Uses **Testcontainers** for isolated PostgreSQL instances
- Implements **connection pooling** with optimized settings
- Batch operations use **NpgsqlBatch** for efficient multi-operation execution
- Includes **clock synchronization** for multi-instance safety
- Features **background cleanup** of expired entries

### Terminology

- **Batch Operations**: SetMultipleAsync/GetMultipleAsync - uses single connection with NpgsqlBatch
- **Individual Operations**: SetAsync/GetAsync - one operation per call
- **Parallel Operations**: Multiple individual operations with Task.WhenAll

### Test Configuration

- **Database**: PostgreSQL 17 Alpine (via Testcontainers)
- **Data Size**: Random values between 100B and 1KB
- **Pre-population**: 1,000 cache entries for read benchmarks
- **Expiration**: 30-minute sliding + 1-hour absolute expiration
- **Connection Pooling**: Enabled with optimized settings

## 📈 Interpreting Results

- **Lower mean times** = better performance
- **Lower memory allocation** = better efficiency
- **Baseline ratio** = relative performance (1.00 = same as baseline, <1.00 = faster, >1.00 = slower)
- **Parallel scaling** should show reasonable scaling with parallelism
- **Batch vs Individual** should show batch operations are faster for larger batch sizes

## 🚀 Performance Optimization Tips

Based on benchmark results, consider:

1. **Use Batch Operations**: For multiple items, use SetMultipleAsync/GetMultipleAsync instead of parallel individual calls
2. **Connection Pool Tuning**: Adjust `MinPoolSize` and `MaxPoolSize` based on workload
3. **Cleanup Frequency**: Increase `ExpiredItemsDeletionInterval` for write-heavy workloads
4. **Clock Sync**: Disable `EnableClockSynchronization` for single-instance deployments
5. **Expiration Strategy**: Use appropriate sliding vs absolute expiration policies

## 🔧 Customizing Benchmarks

To modify the benchmarks:

1. **Change batch sizes**: Update `[Params(10, 50, 100, 500)]` in GlacialCacheBatchBenchmarks
2. **Adjust data sizes**: Modify `GenerateRandomValue()` method
3. **Add custom scenarios**: Create new `[Benchmark]` methods
4. **Configure database**: Update PostgreSQL container settings in `Setup()` methods

## 📝 Example Output

```
BenchmarkDotNet=v0.15.2, OS=Windows 11
Intel Core i7-12700K, 1 CPU, 20 logical and 12 physical cores
.NET 9.0.0, X64 RyuJIT

| Method                      | BatchSize | Mean      | Error    | StdDev   | Ratio | Allocated |
|---------------------------- |---------- |----------:|---------:|---------:|------:|----------:|
| Individual_SetAsync         | 10        | 1,234 µs  | 12.3 µs  | 11.5 µs  | 1.00  | 61.5 KB   |
| Batch_SetMultipleAsync      | 10        | 456 µs    | 4.5 µs   | 4.2 µs   | 0.37  | 12.3 KB   |
| Individual_SetAsync         | 100       | 12,345 µs | 123 µs   | 115 µs   | 1.00  | 615 KB    |
| Batch_SetMultipleAsync      | 100       | 1,234 µs  | 12.3 µs  | 11.5 µs  | 0.10  | 45.6 KB   |
```

_Note: Results shown are illustrative. Actual performance will vary based on hardware and system configuration._

## 🎯 Key Metrics to Track

After running benchmarks, look for:

1. **Batch vs Individual**: How much faster SetMultipleAsync is vs parallel SetAsync calls
2. **MemoryPack vs System.Text.Json**: Serialization performance comparison
3. **Scalability**: How performance scales with batch size and parallelism
4. **Memory efficiency**: Allocation patterns for different operation types
