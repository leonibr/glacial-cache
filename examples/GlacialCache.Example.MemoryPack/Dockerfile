FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the GlacialCache.PostgreSQL project
COPY src/GlacialCache.PostgreSQL ./src/GlacialCache.PostgreSQL/

# Copy the example project files
COPY examples/GlacialCache.Example.MemoryPack/GlacialCache.Example.MemoryPack.csproj ./examples/GlacialCache.Example.MemoryPack/
COPY examples/GlacialCache.Example.MemoryPack/Program.cs ./examples/GlacialCache.Example.MemoryPack/
COPY examples/GlacialCache.Example.MemoryPack/appsettings.json ./examples/GlacialCache.Example.MemoryPack/
# Set working directory to the example project
WORKDIR /src/examples/GlacialCache.Example.MemoryPack

# Restore dependencies
RUN dotnet restore GlacialCache.Example.MemoryPack.csproj

# Build the application
RUN dotnet build GlacialCache.Example.MemoryPack.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish GlacialCache.Example.MemoryPack.csproj -c Release -o /app/publish

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "GlacialCache.Example.MemoryPack.dll"]




