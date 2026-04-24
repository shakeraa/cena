# =============================================================================
# Cena Platform — Database Migrator Docker Image (DB-02)
# Multi-stage build for DbUp migration runner
# =============================================================================

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj src/api/Cena.Db.Migrator/
RUN dotnet restore src/api/Cena.Db.Migrator/Cena.Db.Migrator.csproj

# Copy migration scripts
COPY db/migrations/ db/migrations/

# Copy source code and build
COPY src/api/Cena.Db.Migrator/ src/api/Cena.Db.Migrator/
WORKDIR /src/src/api/Cena.Db.Migrator
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos "" --home "/nonexistent" --shell "/sbin/nologin" --no-create-home --uid 10001 migrator

# Copy published application
COPY --from=build /app/publish .

# Change ownership to non-root user
RUN chown -R migrator:migrator /app

# Switch to non-root user
USER migrator

# Set entrypoint
ENTRYPOINT ["dotnet", "Cena.Db.Migrator.dll"]
