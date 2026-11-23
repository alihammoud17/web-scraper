# Build context is the repository root: docker build -f src/Scraper.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so this layer stays cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Scraper.Domain/Scraper.Domain.csproj src/Scraper.Domain/
COPY src/Scraper.Application/Scraper.Application.csproj src/Scraper.Application/
COPY src/Scraper.Infrastructure/Scraper.Infrastructure.csproj src/Scraper.Infrastructure/
COPY src/Scraper.Contracts/Scraper.Contracts.csproj src/Scraper.Contracts/
COPY src/Scraper.Api/Scraper.Api.csproj src/Scraper.Api/
RUN dotnet restore src/Scraper.Api/Scraper.Api.csproj

COPY src/Scraper.Domain/ src/Scraper.Domain/
COPY src/Scraper.Application/ src/Scraper.Application/
COPY src/Scraper.Infrastructure/ src/Scraper.Infrastructure/
COPY src/Scraper.Contracts/ src/Scraper.Contracts/
COPY src/Scraper.Api/ src/Scraper.Api/
RUN dotnet publish src/Scraper.Api/Scraper.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Run as the image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Scraper.Api.dll"]
