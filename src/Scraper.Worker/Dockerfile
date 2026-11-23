# Build context is the repository root: docker build -f src/Scraper.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so this layer stays cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Scraper.Domain/Scraper.Domain.csproj src/Scraper.Domain/
COPY src/Scraper.Application/Scraper.Application.csproj src/Scraper.Application/
COPY src/Scraper.Infrastructure/Scraper.Infrastructure.csproj src/Scraper.Infrastructure/
COPY src/Scraper.Infrastructure.Browser/Scraper.Infrastructure.Browser.csproj src/Scraper.Infrastructure.Browser/
COPY src/Scraper.Worker/Scraper.Worker.csproj src/Scraper.Worker/
RUN dotnet restore src/Scraper.Worker/Scraper.Worker.csproj

COPY src/Scraper.Domain/ src/Scraper.Domain/
COPY src/Scraper.Application/ src/Scraper.Application/
COPY src/Scraper.Infrastructure/ src/Scraper.Infrastructure/
COPY src/Scraper.Infrastructure.Browser/ src/Scraper.Infrastructure.Browser/
COPY src/Scraper.Worker/ src/Scraper.Worker/
RUN dotnet publish src/Scraper.Worker/Scraper.Worker.csproj -c Release -o /app --no-restore

# The Playwright image ships the browsers plus the .NET runtime. Its tag must equal the
# Microsoft.Playwright package version (Directory.Packages.props): each package version drives one
# exact browser build, and a mismatch fails at launch with "Executable doesn't exist".
FROM mcr.microsoft.com/playwright/dotnet:v1.65.1-noble
WORKDIR /app
COPY --from=build /app .
# pwuser is the non-root user that the Playwright image provides.
USER pwuser
ENTRYPOINT ["dotnet", "Scraper.Worker.dll"]
