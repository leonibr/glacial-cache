Write-Host "🚀 Running Connection Strategy Benchmarks" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "Building project..." -ForegroundColor Yellow
dotnet build -c Release

Write-Host ""
Write-Host "Running benchmarks..." -ForegroundColor Yellow
dotnet run -c Release -- --connection-strategy

Write-Host ""
Write-Host "✅ Benchmarks completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Check the results above for:" -ForegroundColor Cyan
Write-Host "- Connection Pool vs Scoped Connection performance" -ForegroundColor White
Write-Host "- Memory allocation patterns" -ForegroundColor White
Write-Host "- Batch operations efficiency" -ForegroundColor White
Write-Host "- Workload-specific optimizations" -ForegroundColor White
Write-Host ""

Read-Host "Press Enter to continue" 