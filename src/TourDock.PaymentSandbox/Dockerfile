# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.404-alpine3.24 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TourDock.PaymentSandbox/TourDock.PaymentSandbox.csproj src/TourDock.PaymentSandbox/
RUN dotnet restore src/TourDock.PaymentSandbox/TourDock.PaymentSandbox.csproj
COPY src/TourDock.PaymentSandbox/ src/TourDock.PaymentSandbox/
RUN dotnet publish src/TourDock.PaymentSandbox/TourDock.PaymentSandbox.csproj -c Release --no-restore -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-alpine3.24
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Every 2s while starting, then once a minute: each check execs a process
# through dockerd/containerd, which keeps an idle host busy.
HEALTHCHECK --interval=60s --timeout=3s --start-period=60s --start-interval=2s --retries=3 CMD wget --quiet --spider http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "TourDock.PaymentSandbox.dll"]
