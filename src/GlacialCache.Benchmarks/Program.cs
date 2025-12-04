using BenchmarkDotNet.Running;
using GlacialCache.Benchmarks;

// Check if we should run verification test instead of benchmarks
if (args.Length > 0 && args[0] == "--test")
{
    await BatchOperationsTest.RunAsync();
    return;
}

// Run all benchmarks using BenchmarkSwitcher
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args
// , new DebugInProcessConfig() // Uncomment for debugging
);
