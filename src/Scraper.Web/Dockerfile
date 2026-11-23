# Build context is the repository root: docker build -f src/Scraper.Web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so this layer stays cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Scraper.Contracts/Scraper.Contracts.csproj src/Scraper.Contracts/
COPY src/Scraper.Web/Scraper.Web.csproj src/Scraper.Web/
RUN dotnet restore src/Scraper.Web/Scraper.Web.csproj

COPY src/Scraper.Contracts/ src/Scraper.Contracts/
COPY src/Scraper.Web/ src/Scraper.Web/
RUN dotnet publish src/Scraper.Web/Scraper.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Run as the image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Scraper.Web.dll"]
