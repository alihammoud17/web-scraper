# Build context is the repository root: docker build -f tools/Scraper.TestSite/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so this layer stays cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY tools/Scraper.TestSite/Scraper.TestSite.csproj tools/Scraper.TestSite/
RUN dotnet restore tools/Scraper.TestSite/Scraper.TestSite.csproj

COPY tools/Scraper.TestSite/ tools/Scraper.TestSite/
RUN dotnet publish tools/Scraper.TestSite/Scraper.TestSite.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Run as the image's built-in non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Scraper.TestSite.dll"]
