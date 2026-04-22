# =============================================================================
# Cena Student API — Multi-stage Dockerfile (RDY-025)
#
# Build context: repo root
#   docker build -f src/api/Cena.Student.Api.Host/Dockerfile -t cena-student-api .
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src

# Copy csproj + shared project files first for NuGet restore caching.
# Student.Api.Host transitively references Cena.Admin.Api, so include it too.
COPY ["src/api/Cena.Student.Api.Host/Cena.Student.Api.Host.csproj", "src/api/Cena.Student.Api.Host/"]
COPY ["src/api/Cena.Admin.Api/Cena.Admin.Api.csproj", "src/api/Cena.Admin.Api/"]
COPY ["src/api/Cena.Api.Contracts/Cena.Api.Contracts.csproj", "src/api/Cena.Api.Contracts/"]
COPY ["src/actors/Cena.Actors/Cena.Actors.csproj", "src/actors/Cena.Actors/"]
COPY ["src/shared/Cena.Infrastructure/Cena.Infrastructure.csproj", "src/shared/Cena.Infrastructure/"]

RUN dotnet restore src/api/Cena.Student.Api.Host/Cena.Student.Api.Host.csproj

FROM restore AS publish
WORKDIR /src
COPY . .

RUN dotnet publish src/api/Cena.Student.Api.Host/Cena.Student.Api.Host.csproj \
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

RUN groupadd --system --gid 10001 cena \
 && useradd --system --uid 10001 --gid cena --shell /usr/sbin/nologin cena \
 && chown -R cena:cena /app
USER cena:cena

EXPOSE 5051

ENV ASPNETCORE_URLS=http://+:5051 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:5051/health/live || exit 1

ENTRYPOINT ["dotnet", "Cena.Student.Api.Host.dll"]
