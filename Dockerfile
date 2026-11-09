# syntax=docker/dockerfile:1

# --- Frontend: compile the SPA with the same pnpm version as the lockfile ---
FROM node:22-alpine AS frontend
WORKDIR /src

# In the container the API serves the SPA from the same origin, so the bundle
# must use relative /api/* and /media/* paths. The build arg exists only for
# hosts that serve the SPA separately from the API.
ARG VITE_API_URL=/
ENV VITE_API_URL=$VITE_API_URL

RUN corepack enable && corepack prepare pnpm@12.18.0 --activate

# Copy the manifests first so dependency installation is cached independently
# of the source changes.
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY frontend/ ./
RUN pnpm build

# --- Backend: restore, then publish the API ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src

COPY backend/FashionApp.Api/FashionApp.Api.csproj backend/FashionApp.Api/
RUN dotnet restore backend/FashionApp.Api/FashionApp.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/FashionApp.Api/FashionApp.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# --- Runtime: ASP.NET serves both the API and the compiled SPA ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# wget backs the container healthcheck, libgomp1 is required by Magick.NET on
# Linux, and /data is the persistent volume owned by the non-root app user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown -R app:app /data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DATA__ROOT=/data \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=backend --chown=app:app /app/publish/ ./
COPY --from=frontend --chown=app:app /src/dist/ ./wwwroot/

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://localhost:8080/api/health || exit 1

USER app

ENTRYPOINT ["dotnet", "FashionApp.Api.dll"]
