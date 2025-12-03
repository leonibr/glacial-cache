FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the GlacialCache.PostgreSQL project
COPY src/GlacialCache.PostgreSQL ./src/GlacialCache.PostgreSQL/

# Copy the example project files
COPY examples/GlacialCache.Example.CacheEntry/GlacialCache.Example.CacheEntry.csproj ./examples/GlacialCache.Example.CacheEntry/
COPY examples/GlacialCache.Example.CacheEntry/Program.cs ./examples/GlacialCache.Example.CacheEntry/
COPY examples/GlacialCache.Example.CacheEntry/CacheEntryExample.cs ./examples/GlacialCache.Example.CacheEntry/
COPY examples/GlacialCache.Example.CacheEntry/appsettings.json ./examples/GlacialCache.Example.CacheEntry/

# Set working directory to the example project
WORKDIR /src/examples/GlacialCache.Example.CacheEntry

# Restore dependencies
RUN dotnet restore GlacialCache.Example.CacheEntry.csproj

# Build the application
RUN dotnet build GlacialCache.Example.CacheEntry.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish GlacialCache.Example.CacheEntry.csproj -c Release -o /app/publish

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "GlacialCache.Example.CacheEntry.dll"]




