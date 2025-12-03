FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the GlacialCache.PostgreSQL project
COPY src/GlacialCache.PostgreSQL ./src/GlacialCache.PostgreSQL/

# Copy the example project files
COPY examples/GlacialCache.Example.WebApi/GlacialCache.Example.WebApi.csproj ./examples/GlacialCache.Example.WebApi/
COPY examples/GlacialCache.Example.WebApi/Program.cs ./examples/GlacialCache.Example.WebApi/
COPY examples/GlacialCache.Example.WebApi/appsettings.json ./examples/GlacialCache.Example.WebApi/
COPY examples/GlacialCache.Example.WebApi/appsettings.Development.json ./examples/GlacialCache.Example.WebApi/

# Set working directory to the example project
WORKDIR /src/examples/GlacialCache.Example.WebApi

# Restore dependencies
RUN dotnet restore GlacialCache.Example.WebApi.csproj

# Build the application
RUN dotnet build GlacialCache.Example.WebApi.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish GlacialCache.Example.WebApi.csproj -c Release -o /app/publish

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port
EXPOSE 80

# Set the entry point
ENTRYPOINT ["dotnet", "GlacialCache.Example.WebApi.dll"]




