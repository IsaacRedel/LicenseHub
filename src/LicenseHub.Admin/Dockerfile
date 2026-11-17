# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached while only source files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LicenseHub.Admin/LicenseHub.Admin.csproj src/LicenseHub.Admin/
RUN dotnet restore src/LicenseHub.Admin/LicenseHub.Admin.csproj

COPY src/LicenseHub.Admin/ src/LicenseHub.Admin/
RUN dotnet publish src/LicenseHub.Admin/LicenseHub.Admin.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Data Protection keys (cookies, antiforgery). Owned by the non-root app user, so a new named volume mounted
# here inherits that ownership.
RUN mkdir /keys && chown "$APP_UID" /keys

EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "LicenseHub.Admin.dll"]
