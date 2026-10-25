# Build context: repository root. docker build -f infrastructure/docker/api.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Hrms.slnx ./
COPY src/backend ./src/backend
RUN dotnet publish src/backend/Hrms.Api/Hrms.Api.csproj -c Release -o /out /p:UseAppHost=false
RUN dotnet publish src/backend/Hrms.Cli/Hrms.Cli.csproj -c Release -o /out-cli /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
# libgssapi-krb5-2 silences Npgsql's Kerberos probing warning (not used; PostgreSQL auth is password-based).
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys && chown $APP_UID:$APP_UID /keys
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hrms.Api.dll"]

# Operator tooling (migrate-platform, seed-platform, create-tenant, retry-tenant). Secrets via environment only.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS cli
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys && chown $APP_UID:$APP_UID /keys
COPY --from=build /out-cli .
USER $APP_UID
ENTRYPOINT ["dotnet", "Hrms.Cli.dll"]
