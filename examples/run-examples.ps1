param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("basic", "cacheentry", "memorypack", "webapi", "all")]
    [string]$Version = "basic",

    [Parameter(Mandatory = $false)]
    [switch]$Docker,

    [Parameter(Mandatory = $false)]
    [switch]$Clean,

    [Parameter(Mandatory = $false)]
    [switch]$Build,

    [Parameter(Mandatory = $false)]
    [string]$PostgresHost = "localhost",

    [Parameter(Mandatory = $false)]
    [string]$PostgresDb = "glacialcache",

    [Parameter(Mandatory = $false)]
    [string]$PostgresUser = "postgres",

    [Parameter(Mandatory = $false)]
    [string]$PostgresPassword = "password"
)

$ErrorActionPreference = "Stop"

# Function to write colored output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

# Function to check if PostgreSQL is running
function Test-PostgreSQL {
    param([string]$HostName, [string]$Database, [string]$Username, [string]$Password)

    try {
        $connectionString = "Host=$HostName;Database=$Database;Username=$Username;Password=$Password"
        Write-ColorOutput "🔍 Checking PostgreSQL connection..." "Yellow"
        Write-ColorOutput "   Connection: $connectionString" "Gray"

        # Try to connect using psql if available, otherwise assume it's running
        $psqlAvailable = Get-Command psql -ErrorAction SilentlyContinue
        if ($psqlAvailable) {
            $env:PGPASSWORD = $Password
            $result = & psql -h $HostName -U $Username -d $Database -c "SELECT 1;" 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-ColorOutput "✅ PostgreSQL is accessible" "Green"
                return $true
            }
            else {
                Write-ColorOutput "❌ PostgreSQL connection failed" "Red"
                return $false
            }
        }
        else {
            Write-ColorOutput "⚠️  psql not found, assuming PostgreSQL is running" "Yellow"
            return $true
        }
    }
    catch {
        Write-ColorOutput "❌ Error checking PostgreSQL: $_" "Red"
        return $false
    }
}

# Function to run Docker Compose
function Invoke-DockerCompose {
    param([string]$Profile, [string]$Action = "up")

    Write-ColorOutput "🐳 Running Docker Compose with profile: $Profile" "Cyan"

    $dockerAvailable = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $dockerAvailable) {
        Write-ColorOutput "❌ Docker is not installed or not in PATH" "Red"
        exit 1
    }

    $dockerComposeAvailable = Get-Command docker-compose -ErrorAction SilentlyContinue
    if (-not $dockerComposeAvailable) {
        Write-ColorOutput "❌ Docker Compose is not installed or not in PATH" "Red"
        exit 1
    }

    # Check if PostgreSQL is running in Docker
    $postgresRunning = docker ps | Select-String "GlacialCache-postgres"
    if (-not $postgresRunning) {
        Write-ColorOutput "📦 Starting PostgreSQL..." "Yellow"
        & docker-compose up -d postgres
        Start-Sleep -Seconds 10
    }

    # Run the specific example
    if ($Action -eq "up") {
        & docker-compose --profile $Profile up postgres "GlacialCache-$Profile"
    }
    else {
        & docker-compose --profile $Profile $Action
    }
}

# Function to run .NET example
function Invoke-DotNetExample {
    param([string]$ExampleName)

    $projectPath = "GlacialCache.Example.$ExampleName"
    $csprojPath = "$projectPath/GlacialCache.Example.$ExampleName.csproj"

    if (-not (Test-Path $csprojPath)) {
        Write-ColorOutput "❌ Project file not found: $csprojPath" "Red"
        exit 1
    }

    Write-ColorOutput "🔧 Running .NET example: $ExampleName" "Cyan"

    # Set environment variables for database connection
    $env:POSTGRES_HOST = $PostgresHost
    $env:POSTGRES_DB = $PostgresDb
    $env:POSTGRES_USER = $PostgresUser
    $env:POSTGRES_PASSWORD = $PostgresPassword


    # For Web API example, set connection string
    if ($ExampleName -eq "webapi") {
        $env:ConnectionStrings__DefaultConnection = "Host=$PostgresHost;Database=$PostgresDb;Username=$PostgresUser;Password=$PostgresPassword"
    }

    try {
        if ($Build) {
            Write-ColorOutput "🔨 Building project..." "Yellow"
            & dotnet build $csprojPath
            if ($LASTEXITCODE -ne 0) {
                throw "Build failed"
            }
        }

        Write-ColorOutput "🚀 Starting example..." "Green"
        & dotnet run --project $csprojPath
    }
    catch {
        Write-ColorOutput "❌ Error running example: $_" "Red"
        exit 1
    }
}

# Function to clean up
function Invoke-Clean {
    Write-ColorOutput "🧹 Cleaning up..." "Yellow"

    # Stop all Docker containers
    & docker-compose down -v 2>$null

    # Remove bin and obj directories
    Get-ChildItem -Directory -Recurse | Where-Object {
        $_.Name -eq "bin" -or $_.Name -eq "obj"
    } | Remove-Item -Recurse -Force

    Write-ColorOutput "✅ Cleanup completed" "Green"
}

# Main execution logic
Write-ColorOutput "🚀 GlacialCache Examples Runner" "Cyan"
Write-ColorOutput "============================" "Cyan"
Write-ColorOutput ""

# Clean if requested
if ($Clean) {
    Invoke-Clean
    if (-not ($Version -or $Build)) {
        exit 0
    }
}

# Check PostgreSQL connectivity (for non-Docker runs)
if (-not $Docker) {
    $postgresOk = Test-PostgreSQL -HostName $PostgresHost -Database $PostgresDb -Username $PostgresUser -Password $PostgresPassword
    if (-not $postgresOk) {
        Write-ColorOutput "💡 Tip: Start PostgreSQL or use -Docker parameter" "Yellow"
        exit 1
    }
}

# Execute based on version
switch ($Version) {
    "all" {
        Write-ColorOutput "🔄 Running all examples sequentially..." "Cyan"

        $examples = @("basic", "cacheentry", "memorypack")
        foreach ($example in $examples) {
            Write-ColorOutput "`n--- Running $example example ---" "Magenta"
            if ($Docker) {
                Invoke-DockerCompose -Profile $example -Action "up"
            }
            else {
                Invoke-DotNetExample -ExampleName $example
            }
            Write-ColorOutput "--- $example example completed ---`n" "Magenta"
            Start-Sleep -Seconds 2
        }
    }
    "webapi" {
        if ($Docker) {
            Invoke-DockerCompose -Profile "webapi"
            Write-ColorOutput "🌐 Web API will be available at: http://localhost:8080" "Green"
            Write-ColorOutput "📖 Swagger UI: http://localhost:8080/swagger" "Green"
        }
        else {
            Invoke-DotNetExample -ExampleName "webapi"
        }
    }
    default {
        if ($Docker) {
            Invoke-DockerCompose -Profile $Version
        }
        else {
            Invoke-DotNetExample -ExampleName $Version
        }
    }
}

Write-ColorOutput "`n🎉 Examples execution completed!" "Green"




