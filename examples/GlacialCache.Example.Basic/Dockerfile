FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the GlacialCache.PostgreSQL project
COPY src/GlacialCache.PostgreSQL ./src/GlacialCache.PostgreSQL/

# Copy the example project files
COPY examples/GlacialCache.Example.Basic/GlacialCache.Example.Basic.csproj ./examples/GlacialCache.Example.Basic/
COPY examples/GlacialCache.Example.Basic/Program.cs ./examples/GlacialCache.Example.Basic/
COPY examples/GlacialCache.Example.Basic/appsettings.json ./examples/GlacialCache.Example.Basic/
COPY examples/GlacialCache.Example.Basic/appsettings.nonazure.json ./examples/GlacialCache.Example.Basic/

# Set working directory to the example project
WORKDIR /src/examples/GlacialCache.Example.Basic

# Restore dependencies
RUN dotnet restore GlacialCache.Example.Basic.csproj

# Build the application
RUN dotnet build GlacialCache.Example.Basic.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish GlacialCache.Example.Basic.csproj -c Release -o /app/publish

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "GlacialCache.Example.Basic.dll"]
