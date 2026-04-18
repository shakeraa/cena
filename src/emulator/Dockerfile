# =============================================================================
# Cena Student Emulator — Multi-stage Dockerfile (RDY-056 Phase 4)
#
# Simulates student interactions against the actor cluster via NATS.
# Build context: repo root.
#   docker build -f src/emulator/Dockerfile -t cena-emulator .
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src

COPY ["src/emulator/Cena.Emulator.csproj", "src/emulator/"]
COPY ["src/actors/Cena.Actors/Cena.Actors.csproj", "src/actors/Cena.Actors/"]
COPY ["src/shared/Cena.Infrastructure/Cena.Infrastructure.csproj", "src/shared/Cena.Infrastructure/"]
COPY ["src/api/Cena.Api.Contracts/Cena.Api.Contracts.csproj", "src/api/Cena.Api.Contracts/"]
COPY ["src/api/Cena.Admin.Api/Cena.Admin.Api.csproj", "src/api/Cena.Admin.Api/"]

RUN dotnet restore src/emulator/Cena.Emulator.csproj

FROM restore AS publish
WORKDIR /src
COPY . .

RUN dotnet publish src/emulator/Cena.Emulator.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .

RUN groupadd --system --gid 10001 emu \
 && useradd --system --uid 10001 --gid emu --shell /usr/sbin/nologin emu \
 && chown -R emu:emu /app
USER emu:emu

ENV DOTNET_RUNNING_IN_CONTAINER=true

# Default args; overridable in docker-compose env / command.
ENTRYPOINT ["dotnet", "Cena.Emulator.dll"]
CMD ["--students=50", "--speed=25", "--duration=300"]
