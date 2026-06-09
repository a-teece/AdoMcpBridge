# Multi-stage build for the ADO MCP Bridge API host.
# Build context is the repository root (see .github/workflows/release.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj \
        --configuration Release \
        --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AdoMcpBridge.Api.dll"]
