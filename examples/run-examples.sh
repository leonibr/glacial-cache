#!/bin/bash

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Default values
VERSION="basic"
DOCKER=false
CLEAN=false
BUILD=false
POSTGRES_HOST="localhost"
POSTGRES_DB="glacialcache"
POSTGRES_USER="postgres"
POSTGRES_PASSWORD="password"

# Function to print colored output
print_color() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Function to show usage
show_usage() {
    cat << EOF
GlacialCache Examples Runner

USAGE:
    ./run-examples.sh [OPTIONS]

OPTIONS:
    -v, --version VERSION     Example version to run (basic, cacheentry, memorypack, webapi, all)
    -d, --docker              Run using Docker Compose
    -c, --clean               Clean up before running
    -b, --build               Build projects before running
    --postgres-host HOST      PostgreSQL host (default: localhost)
    --postgres-db DB          PostgreSQL database (default: GlacialCache)
    --postgres-user USER      PostgreSQL user (default: postgres)
    --postgres-password PASS  PostgreSQL password (default: password)
    -h, --help                Show this help message

EXAMPLES:
    ./run-examples.sh -v basic -d                    # Run basic example with Docker
    ./run-examples.sh -v webapi                      # Run web API example
    ./run-examples.sh -v all -d -c                   # Run all examples with Docker, clean first
    ./run-examples.sh --postgres-host mydb -v basic  # Run basic example with custom DB host

EOF
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -d|--docker)
            DOCKER=true
            shift
            ;;
        -c|--clean)
            CLEAN=true
            shift
            ;;
        -b|--build)
            BUILD=true
            shift
            ;;
        --postgres-host)
            POSTGRES_HOST="$2"
            shift 2
            ;;
        --postgres-db)
            POSTGRES_DB="$2"
            shift 2
            ;;
        --postgres-user)
            POSTGRES_USER="$2"
            shift 2
            ;;
        --postgres-password)
            POSTGRES_PASSWORD="$2"
            shift 2
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            print_color $RED "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Validate version
case $VERSION in
    basic|cacheentry|memorypack|webapi|all)
        ;;
    *)
        print_color $RED "Invalid version: $VERSION"
        print_color $YELLOW "Valid versions: basic, cacheentry, memorypack, webapi, all"
        exit 1
        ;;
esac

# Function to check PostgreSQL connectivity
check_postgresql() {
    print_color $YELLOW "🔍 Checking PostgreSQL connection..."
    print_color $BLUE "   Connection: Host=$POSTGRES_HOST;Database=$POSTGRES_DB;Username=$POSTGRES_USER"

    if command -v psql &> /dev/null; then
        export PGPASSWORD="$POSTGRES_PASSWORD"
        if psql -h "$POSTGRES_HOST" -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT 1;" &> /dev/null; then
            print_color $GREEN "✅ PostgreSQL is accessible"
            return 0
        else
            print_color $RED "❌ PostgreSQL connection failed"
            return 1
        fi
    else
        print_color $YELLOW "⚠️  psql not found, assuming PostgreSQL is running"
        return 0
    fi
}

# Function to run Docker Compose
run_docker_compose() {
    local profile=$1
    local action=${2:-up}

    print_color $CYAN "🐳 Running Docker Compose with profile: $profile"

    if ! command -v docker &> /dev/null; then
        print_color $RED "❌ Docker is not installed or not in PATH"
        exit 1
    fi

    if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
        print_color $RED "❌ Docker Compose is not installed or not in PATH"
        exit 1
    fi

    # Check if PostgreSQL is running in Docker
    if ! docker ps | grep -q "GlacialCache-postgres"; then
        print_color $YELLOW "📦 Starting PostgreSQL..."
        if command -v docker-compose &> /dev/null; then
            docker-compose up -d postgres
        else
            docker compose up -d postgres
        fi
        sleep 10
    fi

    # Run the specific example
    if command -v docker-compose &> /dev/null; then
        docker-compose --profile "$profile" "$action" postgres "GlacialCache-$profile"
    else
        docker compose --profile "$profile" "$action" postgres "GlacialCache-$profile"
    fi
}

# Function to run .NET example
run_dotnet_example() {
    local example_name=$1
    local project_path="GlacialCache.Example.$example_name"
    local csproj_path="$project_path/GlacialCache.Example.$example_name.csproj"

    if [[ ! -f "$csproj_path" ]]; then
        print_color $RED "❌ Project file not found: $csproj_path"
        exit 1
    fi

    print_color $CYAN "🔧 Running .NET example: $example_name"

    # Set environment variables for database connection
    export POSTGRES_HOST="$POSTGRES_HOST"
    export POSTGRES_DB="$POSTGRES_DB"
    export POSTGRES_USER="$POSTGRES_USER"
    export POSTGRES_PASSWORD="$POSTGRES_PASSWORD"


    # For Web API example, set connection string
    if [[ "$example_name" == "webapi" ]]; then
        export ConnectionStrings__DefaultConnection="Host=$POSTGRES_HOST;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
    fi

    if [[ "$BUILD" == true ]]; then
        print_color $YELLOW "🔨 Building project..."
        dotnet build "$csproj_path"
    fi

    print_color $GREEN "🚀 Starting example..."
    dotnet run --project "$csproj_path"
}

# Function to clean up
cleanup() {
    print_color $YELLOW "🧹 Cleaning up..."

    # Stop all Docker containers
    if command -v docker-compose &> /dev/null; then
        docker-compose down -v 2>/dev/null || true
    elif docker compose version &> /dev/null; then
        docker compose down -v 2>/dev/null || true
    fi

    # Remove bin and obj directories
    find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true

    print_color $GREEN "✅ Cleanup completed"
}

# Main execution
print_color $CYAN "🚀 GlacialCache Examples Runner"
print_color $CYAN "============================"
echo

# Clean if requested
if [[ "$CLEAN" == true ]]; then
    cleanup
    if [[ "$VERSION" == "basic" && "$BUILD" == false && "$DOCKER" == false ]]; then
        exit 0
    fi
fi

# Check PostgreSQL connectivity (for non-Docker runs)
if [[ "$DOCKER" == false ]]; then
    if ! check_postgresql; then
        print_color $YELLOW "💡 Tip: Start PostgreSQL or use -d/--docker parameter"
        exit 1
    fi
fi

# Execute based on version
case $VERSION in
    all)
        print_color $CYAN "🔄 Running all examples sequentially..."

        examples=("basic" "cacheentry" "memorypack")
        for example in "${examples[@]}"; do
            print_color $MAGENTA "\n--- Running $example example ---"
            if [[ "$DOCKER" == true ]]; then
                run_docker_compose "$example"
            else
                run_dotnet_example "$example"
            fi
            print_color $MAGENTA "--- $example example completed ---"
            sleep 2
        done
        ;;
    webapi)
        if [[ "$DOCKER" == true ]]; then
            run_docker_compose "webapi"
            print_color $GREEN "🌐 Web API will be available at: http://localhost:8080"
            print_color $GREEN "📖 Swagger UI: http://localhost:8080/swagger"
        else
            run_dotnet_example "webapi"
        fi
        ;;
    *)
        if [[ "$DOCKER" == true ]]; then
            run_docker_compose "$VERSION"
        else
            run_dotnet_example "$VERSION"
        fi
        ;;
esac

print_color $GREEN "\n🎉 Examples execution completed!"




