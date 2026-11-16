# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached while only source files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LicenseHub.Domain/LicenseHub.Domain.csproj src/LicenseHub.Domain/
COPY src/LicenseHub.Application/LicenseHub.Application.csproj src/LicenseHub.Application/
COPY src/LicenseHub.Infrastructure/LicenseHub.Infrastructure.csproj src/LicenseHub.Infrastructure/
COPY src/LicenseHub.Api/LicenseHub.Api.csproj src/LicenseHub.Api/
RUN dotnet restore src/LicenseHub.Api/LicenseHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/LicenseHub.Api/LicenseHub.Api.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "LicenseHub.Api.dll"]
