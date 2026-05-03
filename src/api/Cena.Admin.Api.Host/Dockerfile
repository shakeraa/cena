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

# ── Stage: dev (hotreload target — used by docker-compose.hotreload.yml) ─────
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

# ── Stage: runtime (production) ──────────────────────────────────────────────
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

# Bake the runtime data files used by services that read directly from
# the repo at boot. Both BagrutTaxonomyCatalog (ADR-0062 Phase 0) and
# the existing TaxonomyCache walk up from AppContext.BaseDirectory
# looking for `scripts/bagrut-taxonomy.json`. Without this COPY the
# walk reaches the filesystem root and the loader throws
# FileNotFoundException at first request. Keep the canonical path so
# the loaders find the file at /app/scripts/bagrut-taxonomy.json
# (AppContext.BaseDirectory == /app at runtime).
COPY --from=publish /src/scripts/bagrut-taxonomy.json /app/scripts/bagrut-taxonomy.json

RUN groupadd --system --gid 10001 cena \
 && useradd --system --uid 10001 --gid cena --shell /usr/sbin/nologin cena \
 && chown -R cena:cena /app \
 && mkdir -p /var/cena/source-pdfs /var/cena/ocr-figures \
 && chown -R cena:cena /var/cena
# Pre-create + chown the bind targets BEFORE the USER directive so a
# freshly-created named volume mounted at /var/cena/source-pdfs (or
# /var/cena/ocr-figures) inherits cena:cena ownership on first mount.
# Without this, Docker auto-creates the mount point as root:root, the
# cena process can't write, and BagrutPdfStore.PersistAsync fails with
# UnauthorizedAccessException — silently swallowed by the catch in
# BagrutPdfIngestionService and surfaced to the curator as
# "Visual review unavailable" on freshly-uploaded items
# (2026-05-01 user report).
USER cena:cena

EXPOSE 5052

# RDY-064 / ADR-0058 §3: release correlation (see Actors.Host/Dockerfile).
ARG CENA_GIT_SHA=unknown
ENV ASPNETCORE_URLS=http://+:5052 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false \
    CENA__ErrorAggregator__Release=$CENA_GIT_SHA \
    CENA__Cluster__ServiceVersion=$CENA_GIT_SHA

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:5052/health/live || exit 1

ENTRYPOINT ["dotnet", "Cena.Admin.Api.Host.dll"]
