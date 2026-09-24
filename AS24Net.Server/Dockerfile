# The application is framework-dependent (no native code), so it is compiled once on the build machine's
# platform and the result is copied into the runtime image of each target platform (amd64, arm64).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "nuget.config", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["AS24Net.Server/AS24Net.Server.csproj", "AS24Net.Server/"]
COPY ["AS24Net.Core/AS24Net.Core.csproj", "AS24Net.Core/"]
COPY ["AS24Net.DataLayer/AS24Net.DataLayer.csproj", "AS24Net.DataLayer/"]
COPY ["AS24Net.DependencyInjection/AS24Net.DependencyInjection.csproj", "AS24Net.DependencyInjection/"]
COPY ["AS24Net.Domain/AS24Net.Domain.csproj", "AS24Net.Domain/"]
COPY ["AS24Net.Entity/AS24Net.Entity.csproj", "AS24Net.Entity/"]
COPY ["AS24Net.Services/AS24Net.Services.csproj", "AS24Net.Services/"]
RUN dotnet restore "AS24Net.Server/AS24Net.Server.csproj"
COPY . .
RUN dotnet publish "AS24Net.Server/AS24Net.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
        /p:UseAppHost=false --no-restore \
    && mkdir -p /app/data

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# Web UI, REST API and the AS2 endpoint (/as2)
EXPOSE 8080
COPY --from=build /app/publish .
# Persistent data (received and outgoing payloads, archive, data protection keys, logs), writable by the non-root app user.
COPY --from=build --chown=$APP_UID:$APP_UID /app/data /data
ENV DataDirectory=/data \
    LogDirectory=/data/logs \
    DataProtection__KeysDirectory=/data/keys
VOLUME ["/data"]
USER $APP_UID
# Liveness only (the process answers), so that a database outage does not restart the container. The image has
# no curl, bash talks HTTP itself.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "AS24Net.Server.dll"]
