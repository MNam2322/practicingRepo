# The whole app in one image: the .NET API, serving the built React app from wwwroot. Library 11.3 "A Dockerfile that
# isn't wrong": multi-stage, restore cached apart from the source, the runtime image (not the SDK), a non-root user,
# pinned versions, and no secrets inside (they come from the environment when the container starts).
#
#   docker compose up --build        (repo root: the app on http://localhost:8080, with PostgreSQL)

# ---- 1. The web app: npm ci first (cached until package-lock.json changes), then the build ----
FROM node:22.23.3-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---- 2. The API: restore from the project files only (cached until a package changes), then publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS api
WORKDIR /src
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props ./
COPY backend/src/BookingApp.Core/BookingApp.Core.csproj src/BookingApp.Core/
COPY backend/src/BookingApp.Infrastructure/BookingApp.Infrastructure.csproj src/BookingApp.Infrastructure/
COPY backend/src/BookingApp.Api/BookingApp.Api.csproj src/BookingApp.Api/
RUN dotnet restore src/BookingApp.Api/BookingApp.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/BookingApp.Api/BookingApp.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false
# Folders the app writes to, owned by the non-root user (the runtime image has no shell to create them).
RUN mkdir -p /data/keys /data/emails && chown -R 1654:1654 /data

# ---- 3. Runtime: chiseled (no shell, no package manager, non-root), "extra" for the time-zone data and ICU ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled-extra
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
COPY --from=api --chown=1654:1654 /data /data
USER 1654
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/data/keys \
    Email__Folder=/data/emails
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookingApp.Api.dll"]
