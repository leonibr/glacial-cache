using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using GlacialCache.Benchmarks;

// Check if we should run tests instead of benchmarks
if (args.Length > 0 && args[0] == "--test")
{
    await BatchOperationsTest.RunAsync();
    return;
}

// Check if we should run connection strategy benchmarks specifically
if (args.Length > 0 && args[0] == "--connection-strategy")
{
    Console.WriteLine("🚀 Running Connection Strategy Benchmarks");
    Console.WriteLine("=========================================");
    Console.WriteLine();
    Console.WriteLine("This benchmark will compare:");
    Console.WriteLine("1. Connection Pool Strategy - Each operation gets its own connection");
    Console.WriteLine("2. Scoped Connection Strategy - Multiple operations share one connection");
    Console.WriteLine("3. Batch Operations - Bulk operations with optimized batching");
    Console.WriteLine();
    Console.WriteLine("Test scenarios:");
    Console.WriteLine("- Individual operations (1, 5, 10, 20 operations per scope)");
    Console.WriteLine("- Mixed Get/Set operations");
    Console.WriteLine("- Read-heavy workloads");
    Console.WriteLine("- Write-heavy workloads");
    Console.WriteLine("- Batch operations");
    Console.WriteLine();
    Console.WriteLine("Starting benchmarks...");
    Console.WriteLine();

    var summary = BenchmarkRunner.Run<ConnectionStrategyBenchmarks>();

    Console.WriteLine();
    Console.WriteLine("✅ Benchmarks completed!");
    Console.WriteLine();
    Console.WriteLine("Key findings to look for:");
    Console.WriteLine("- Connection Pool vs Scoped Connection performance crossover point");
    Console.WriteLine("- Memory allocation patterns");
    Console.WriteLine("- Batch operations efficiency");
    Console.WriteLine("- Workload-specific optimizations");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args
// , new DebugInProcessConfig()
);

