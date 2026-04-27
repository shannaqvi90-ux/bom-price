# syntax=docker/dockerfile:1.6
# Multi-stage Dockerfile for BomPriceApproval.API (.NET 8)
# Targets: Fly.io free tier (256 MB RAM, ARM or x86). Build happens on the
# build host; runtime is the slim aspnet image.

ARG DOTNET_VERSION=8.0

# ── build stage ──
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy solution + csproj files first for dependency-cache-friendly build.
# (csproj-only restore cached separately from source recompile.)
COPY BomPriceApproval.sln ./
COPY BomPriceApproval.API/BomPriceApproval.API.csproj BomPriceApproval.API/
COPY BomPriceApproval.Tests/BomPriceApproval.Tests.csproj BomPriceApproval.Tests/

RUN dotnet restore BomPriceApproval.API/BomPriceApproval.API.csproj

# Now copy the rest and publish.
COPY BomPriceApproval.API/ BomPriceApproval.API/
RUN dotnet publish BomPriceApproval.API/BomPriceApproval.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

# ── runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Run as non-root for safety (UID 1000 — already exists in the base image).
RUN useradd -m -u 1000 app && chown -R app:app /app
USER app

COPY --from=build --chown=app:app /app/publish ./

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Healthcheck — Program.cs needs a /health endpoint (added in CORS commit).
# fly.toml will also configure HTTP healthchecks externally.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "BomPriceApproval.API.dll"]
