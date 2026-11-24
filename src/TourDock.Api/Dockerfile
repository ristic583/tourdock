# syntax=docker/dockerfile:1

# ---- Build stage: full SDK, never shipped ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.404-alpine3.24 AS build
WORKDIR /src

# Restore first, using only project/build files, so the (slow) restore layer
# stays cached until a dependency actually changes.
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY src/TourDock.Domain/TourDock.Domain.csproj src/TourDock.Domain/
COPY src/TourDock.Application/TourDock.Application.csproj src/TourDock.Application/
COPY src/TourDock.Infrastructure/TourDock.Infrastructure.csproj src/TourDock.Infrastructure/
COPY src/TourDock.Api/TourDock.Api.csproj src/TourDock.Api/
RUN dotnet restore src/TourDock.Api/TourDock.Api.csproj && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/TourDock.Api/TourDock.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---- Migrations bundle: a single executable that applies pending EF Core migrations ----
FROM build AS migrations-build
RUN dotnet ef migrations bundle \
    --project src/TourDock.Infrastructure \
    --startup-project src/TourDock.Infrastructure \
    --configuration Release \
    --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-alpine3.24 AS migrations
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
USER $APP_UID
# Reads the connection string from ConnectionStrings__Database
ENTRYPOINT ["./efbundle"]

# ---- Runtime stage: slim ASP.NET runtime only ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-alpine3.24 AS runtime
# IANA time zone data: tours run in their local time zone (e.g. Europe/Zagreb)
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=build /app/publish .

# Run as the image's built-in non-root user
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Every 2s while starting, then once a minute: each check execs a process
# through dockerd/containerd, which keeps an idle host busy.
HEALTHCHECK --interval=60s --timeout=3s --start-period=60s --start-interval=2s --retries=3 \
    CMD wget --quiet --spider http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "TourDock.Api.dll"]
