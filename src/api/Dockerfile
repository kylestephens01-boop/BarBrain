# BarBrain API — multi-stage build. Build context is the REPO ROOT so the
# project reference to src/shared and Directory.Build.props resolve.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching) — copy only what restore needs.
COPY Directory.Build.props ./
COPY src/shared/BarBrain.Shared.csproj src/shared/
COPY src/api/BarBrain.Api.csproj src/api/
RUN dotnet restore src/api/BarBrain.Api.csproj

# Then the sources (+ the data-source license registry, which the csproj
# embeds so the ADR-024 import gate travels inside the binary).
COPY src/shared/ src/shared/
COPY src/api/ src/api/
COPY docs/DATA-SOURCES.md docs/

# Git SHA is surfaced by /health and /version (BuildInfo). CI passes --build-arg.
ARG GIT_SHA=local
RUN dotnet publish src/api/BarBrain.Api.csproj -c Release -o /app --no-restore \
    -p:SourceRevisionId=$GIT_SHA

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user baked into the .NET image.
USER $APP_UID

ARG GIT_SHA=local
ENV GIT_SHA=$GIT_SHA
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BarBrain.Api.dll"]
