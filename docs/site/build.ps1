# GlacialCache Landing Page Build Script
# This script minifies HTML, CSS, and JavaScript for production deployment

Write-Host "🏗️  Building GlacialCache Landing Page for Production" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

# Check if required tools are installed
function Test-Command {
    param($Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

Write-Host "Checking build tools..." -ForegroundColor Yellow

if (-not (Test-Command "cleancss")) {
    Write-Host "❌ clean-css-cli not found. Installing..." -ForegroundColor Red
    npm install -g clean-css-cli
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install clean-css-cli. Please install Node.js and npm first." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Command "html-minifier")) {
    Write-Host "❌ html-minifier not found. Installing..." -ForegroundColor Red
    npm install -g html-minifier
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install html-minifier. Please install Node.js and npm first." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Command "terser")) {
    Write-Host "❌ terser not found. Installing..." -ForegroundColor Red
    npm install -g terser
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install terser. Please install Node.js and npm first." -ForegroundColor Red
        exit 1
    }
}

Write-Host "✅ Build tools ready" -ForegroundColor Green
Write-Host ""

# Minify CSS
Write-Host "📦 Minifying CSS..." -ForegroundColor Yellow
cleancss -o styles.min.css styles.css
if ($LASTEXITCODE -eq 0) {
    $originalSize = (Get-Item styles.css).Length
    $minifiedSize = (Get-Item styles.min.css).Length
    $savings = [math]::Round(($originalSize - $minifiedSize) / $originalSize * 100, 2)
    Write-Host "✅ CSS minified: styles.css → styles.min.css" -ForegroundColor Green
    Write-Host "   Original: $($originalSize) bytes, Minified: $($minifiedSize) bytes, Saved: $savings%" -ForegroundColor Gray
} else {
    Write-Host "❌ CSS minification failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Minify JavaScript
Write-Host "📦 Minifying JavaScript..." -ForegroundColor Yellow
terser main.js -o main.min.js --compress --mangle
if ($LASTEXITCODE -eq 0) {
    $originalSize = (Get-Item main.js).Length
    $minifiedSize = (Get-Item main.min.js).Length
    $savings = [math]::Round(($originalSize - $minifiedSize) / $originalSize * 100, 2)
    Write-Host "✅ JavaScript minified: main.js → main.min.js" -ForegroundColor Green
    Write-Host "   Original: $($originalSize) bytes, Minified: $($minifiedSize) bytes, Saved: $savings%" -ForegroundColor Gray
} else {
    Write-Host "❌ JavaScript minification failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Minify HTML
Write-Host "📦 Minifying HTML..." -ForegroundColor Yellow
html-minifier --collapse-whitespace --remove-comments --minify-css true --minify-js true -o index.min.html index.html
if ($LASTEXITCODE -eq 0) {
    $originalSize = (Get-Item index.html).Length
    $minifiedSize = (Get-Item index.min.html).Length
    $savings = [math]::Round(($originalSize - $minifiedSize) / $originalSize * 100, 2)
    Write-Host "✅ HTML minified: index.html → index.min.html" -ForegroundColor Green
    Write-Host "   Original: $($originalSize) bytes, Minified: $($minifiedSize) bytes, Saved: $savings%" -ForegroundColor Gray
} else {
    Write-Host "❌ HTML minification failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Minify 404 page
Write-Host "📦 Minifying 404 page..." -ForegroundColor Yellow
html-minifier --collapse-whitespace --remove-comments --minify-css true --minify-js true -o 404.min.html 404.html
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ 404 page minified: 404.html → 404.min.html" -ForegroundColor Green
} else {
    Write-Host "⚠️  404 page minification failed (non-critical)" -ForegroundColor Yellow
}

Write-Host ""

# Create production dist folder
$distFolder = "dist"
if (Test-Path $distFolder) {
    Write-Host "🗑️  Cleaning existing dist folder..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $distFolder
}

Write-Host "📁 Creating production dist folder..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $distFolder | Out-Null

# Copy minified files to dist
Copy-Item "index.min.html" "$distFolder/index.html"
Copy-Item "404.min.html" "$distFolder/404.html" -ErrorAction SilentlyContinue
Copy-Item "styles.min.css" "$distFolder/styles.css"
Copy-Item "main.min.js" "$distFolder/main.js"

# Copy favicon and manifest files
Write-Host "📋 Copying favicon and manifest files..." -ForegroundColor Yellow
Copy-Item "favicon.svg" "$distFolder/favicon.svg"
Copy-Item "favicon-96x96.png" "$distFolder/favicon-96x96.png"
Copy-Item "favicon.ico" "$distFolder/favicon.ico"
Copy-Item "apple-touch-icon.png" "$distFolder/apple-touch-icon.png"
Copy-Item "web-app-manifest-192x192.png" "$distFolder/web-app-manifest-192x192.png"
Copy-Item "web-app-manifest-512x512.png" "$distFolder/web-app-manifest-512x512.png"
Copy-Item "site.webmanifest" "$distFolder/site.webmanifest"
Copy-Item "robots.txt" "$distFolder/robots.txt"
Copy-Item "sitemap.xml" "$distFolder/sitemap.xml"

Write-Host "✅ Production files copied to $distFolder/" -ForegroundColor Green
Write-Host ""

# Calculate total size savings
$totalOriginal = (Get-Item styles.css).Length + (Get-Item main.js).Length + (Get-Item index.html).Length
$totalMinified = (Get-Item styles.min.css).Length + (Get-Item main.min.js).Length + (Get-Item index.min.html).Length
$totalSavings = [math]::Round(($totalOriginal - $totalMinified) / $totalOriginal * 100, 2)

# Summary
Write-Host "🎉 Build Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Production files are ready in the '$distFolder' folder:" -ForegroundColor Cyan
Write-Host "  - index.html (minified)" -ForegroundColor Gray
Write-Host "  - 404.html (minified)" -ForegroundColor Gray
Write-Host "  - styles.css (minified)" -ForegroundColor Gray
Write-Host "  - main.js (minified)" -ForegroundColor Gray
Write-Host "  - All favicon and manifest files" -ForegroundColor Gray
Write-Host ""
Write-Host "Total size reduction: $totalSavings%" -ForegroundColor Green
Write-Host "  Original: $totalOriginal bytes" -ForegroundColor Gray
Write-Host "  Minified: $totalMinified bytes" -ForegroundColor Gray
Write-Host "  Saved: $($totalOriginal - $totalMinified) bytes" -ForegroundColor Gray
Write-Host ""
Write-Host "To deploy:" -ForegroundColor Yellow
Write-Host "  1. Upload contents of '$distFolder' folder to your web server" -ForegroundColor Gray
Write-Host "  2. Or commit '$distFolder' to gh-pages branch for GitHub Pages" -ForegroundColor Gray
Write-Host ""
Write-Host "To test locally:" -ForegroundColor Yellow
Write-Host "  cd $distFolder" -ForegroundColor Gray
Write-Host "  python -m http.server 8000" -ForegroundColor Gray
Write-Host "  # Then open http://localhost:8000" -ForegroundColor Gray
Write-Host ""

