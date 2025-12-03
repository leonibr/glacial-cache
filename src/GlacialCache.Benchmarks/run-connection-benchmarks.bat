@echo off
echo 🚀 Running Connection Strategy Benchmarks
echo =========================================
echo.

echo Building project...
dotnet build -c Release

echo.
echo Running benchmarks...
dotnet run -c Release -- --connection-strategy

echo.
echo ✅ Benchmarks completed!
echo.
echo Check the results above for:
echo - Connection Pool vs Scoped Connection performance
echo - Memory allocation patterns  
echo - Batch operations efficiency
echo - Workload-specific optimizations
echo.
pause 