# BarBrain web (Blazor WASM PWA) — published to static assets and served by
# Caddy, which also reverse-proxies API routes so the SPA is same-origin with
# the API (no CORS, mirrors the prod topology: Caddy in front). Build context
# is the REPO ROOT (project reference to src/shared, Directory.Build.props).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/shared/BarBrain.Shared.csproj src/shared/
COPY src/web/BarBrain.Web.csproj src/web/
RUN dotnet restore src/web/BarBrain.Web.csproj

COPY src/shared/ src/shared/
COPY src/web/ src/web/
RUN dotnet publish src/web/BarBrain.Web.csproj -c Release -o /app --no-restore

FROM caddy:2-alpine AS final
COPY infra/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/wwwroot /srv
EXPOSE 80
