# =============================================================================
# Cena Admin API — Multi-stage Dockerfile (RDY-025)
#
# Build context: repo root
#   docker build -f src/api/Cena.Admin.Api.Host/Dockerfile -t cena-admin-api .
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src

COPY ["src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj", "src/api/Cena.Admin.Api.Host/"]
COPY ["src/api/Cena.Admin.Api/Cena.Admin.Api.csproj", "src/api/Cena.Admin.Api/"]
COPY ["src/api/Cena.Api.Contracts/Cena.Api.Contracts.csproj", "src/api/Cena.Api.Contracts/"]
COPY ["src/actors/Cena.Actors/Cena.Actors.csproj", "src/actors/Cena.Actors/"]
COPY ["src/shared/Cena.Infrastructure/Cena.Infrastructure.csproj", "src/shared/Cena.Infrastructure/"]

RUN dotnet restore src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj

FROM restore AS publish
WORKDIR /src
COPY . .

RUN dotnet publish src/api/Cena.Admin.Api.Host/Cena.Admin.Api.Host.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:SkipOpenApiGeneration=true

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

RUN groupadd --system --gid 10001 cena \
 && useradd --system --uid 10001 --gid cena --shell /usr/sbin/nologin cena \
 && chown -R cena:cena /app
USER cena:cena

EXPOSE 5052

ENV ASPNETCORE_URLS=http://+:5052 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:5052/health/live || exit 1

ENTRYPOINT ["dotnet", "Cena.Admin.Api.Host.dll"]
