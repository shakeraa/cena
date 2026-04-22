# =============================================================================
# Cena Actor Host — Multi-stage Dockerfile (RDY-025)
#
# Layers:
#   1. restore — NuGet restore only (caches well; invalidated only on .csproj changes)
#   2. build   — compile + test
#   3. publish — publish self-contained trim-ready artifact
#   4. runtime — minimal ASP.NET runtime image, non-root user
#
# Build context: repo root (to access .csproj siblings and shared Cena.Infrastructure)
#   docker build -f src/actors/Cena.Actors.Host/Dockerfile -t cena-actors-host .
# =============================================================================

# ── Stage 1: restore ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src

# Copy csproj files first for better layer caching — NuGet restore only invalidates
# when a .csproj or Directory.Packages.props changes, not on source edits.
COPY ["src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj", "src/actors/Cena.Actors.Host/"]
COPY ["src/actors/Cena.Actors/Cena.Actors.csproj", "src/actors/Cena.Actors/"]
COPY ["src/shared/Cena.Infrastructure/Cena.Infrastructure.csproj", "src/shared/Cena.Infrastructure/"]
COPY ["src/api/Cena.Api.Contracts/Cena.Api.Contracts.csproj", "src/api/Cena.Api.Contracts/"]
# Actor host references Cena.Admin.Api for shared service contracts.
COPY ["src/api/Cena.Admin.Api/Cena.Admin.Api.csproj", "src/api/Cena.Admin.Api/"]

RUN dotnet restore src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj

# ── Stage 2: build + publish ──────────────────────────────────────────────────
FROM restore AS publish
WORKDIR /src
COPY . .

RUN dotnet publish src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:SkipOpenApiGeneration=true

# ── Stage 3: dev (hotreload target — used by docker-compose.hotreload.yml) ───
# SDK-based image so `dotnet watch` works; carries the same OCR toolchain as
# the runtime stage below so dev and prod cannot drift on OS-level tools.
# Compose overlay supplies volume mounts, working dir, and the watch command.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      curl \
      tesseract-ocr \
      tesseract-ocr-heb \
      tesseract-ocr-eng \
      poppler-utils \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src
# ENTRYPOINT / CMD intentionally omitted — docker-compose.hotreload.yml
# overrides them with `dotnet watch`.

# ── Stage 4: runtime ──────────────────────────────────────────────────────────
# OCR toolchain list is identical to the `dev` stage above — keep them in sync.
# Layer 0 (pdftoppm) and Layer 2a (tesseract) run in-process via Process.Start
# per ADR-0033. TesseractLocalRunner fail-fasts at construction if these
# binaries are missing, so they are a hard prod requirement, not optional.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      curl \
      tesseract-ocr \
      tesseract-ocr-heb \
      tesseract-ocr-eng \
      poppler-utils \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Non-root user (uid 10001 matches podSecurityContext.runAsUser in Helm chart)
RUN groupadd --system --gid 10001 cena \
 && useradd --system --uid 10001 --gid cena --shell /usr/sbin/nologin cena \
 && chown -R cena:cena /app
USER cena:cena

# Actor cluster advertised port (Proto.Cluster gossip)
EXPOSE 8090
# HTTP port for health probes + metrics
EXPOSE 5050

ENV ASPNETCORE_URLS=http://+:5050 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

# Healthcheck targets /health/live (liveness probe endpoint from Program.cs)
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:5050/health/live || exit 1

ENTRYPOINT ["dotnet", "Cena.Actors.Host.dll"]
