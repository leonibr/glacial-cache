#!/usr/bin/env pwsh
# Quick start script for viewing the GlacialCache landing page locally

Write-Host "🌐 Starting GlacialCache Landing Page..." -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

$port = 8000

Write-Host "📁 Location: $scriptPath" -ForegroundColor Gray
Write-Host "🔌 Port: $port" -ForegroundColor Gray
Write-Host ""

# Check for available web servers
$pythonAvailable = Get-Command python -ErrorAction SilentlyContinue
$dotnetServeAvailable = Get-Command dotnet-serve -ErrorAction SilentlyContinue
$httpServerAvailable = Get-Command http-server -ErrorAction SilentlyContinue

if ($pythonAvailable) {
    Write-Host "✅ Using Python HTTP server" -ForegroundColor Green
    Write-Host ""
    Write-Host "🌐 Opening browser to http://localhost:$port" -ForegroundColor Cyan
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    Write-Host ""
    
    Start-Sleep -Seconds 1
    Start-Process "http://localhost:$port"
    
    python -m http.server $port
}
elseif ($dotnetServeAvailable) {
    Write-Host "✅ Using dotnet-serve" -ForegroundColor Green
    Write-Host ""
    Write-Host "🌐 Opening browser to http://localhost:$port" -ForegroundColor Cyan
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    Write-Host ""
    
    Start-Sleep -Seconds 1
    Start-Process "http://localhost:$port"
    
    dotnet serve -p $port
}
elseif ($httpServerAvailable) {
    Write-Host "✅ Using http-server (Node.js)" -ForegroundColor Green
    Write-Host ""
    Write-Host "🌐 Opening browser to http://localhost:$port" -ForegroundColor Cyan
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    Write-Host ""
    
    Start-Sleep -Seconds 1
    Start-Process "http://localhost:$port"
    
    http-server -p $port
}
else {
    Write-Host "❌ No web server found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install one of the following:" -ForegroundColor Yellow
    Write-Host "  • Python 3: https://www.python.org/" -ForegroundColor Gray
    Write-Host "  • dotnet-serve: dotnet tool install -g dotnet-serve" -ForegroundColor Gray
    Write-Host "  • http-server: npm install -g http-server" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Or simply open index.html directly in your browser:" -ForegroundColor Yellow
    Write-Host "  start index.html" -ForegroundColor Gray
    Write-Host ""
    
    $response = Read-Host "Open index.html in browser now? (Y/n)"
    if ($response -ne 'n' -and $response -ne 'N') {
        Start-Process "index.html"
    }
}

