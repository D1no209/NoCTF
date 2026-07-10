# Frontend build stage
FROM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend

COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile

COPY frontend/ ./
RUN bun run build

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0.422 AS build
WORKDIR /src/backend

# Copy solution and project files for layer caching
COPY backend/NoCTF.slnx ./
COPY backend/src/NoCTF.API/NoCTF.API.csproj src/NoCTF.API/
COPY backend/src/NoCTF.Application/NoCTF.Application.csproj src/NoCTF.Application/
COPY backend/src/NoCTF.Container.Docker/NoCTF.Container.Docker.csproj src/NoCTF.Container.Docker/
COPY backend/src/NoCTF.Container.K8s/NoCTF.Container.K8s.csproj src/NoCTF.Container.K8s/
COPY backend/src/NoCTF.Core/NoCTF.Core.csproj src/NoCTF.Core/
COPY backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj src/NoCTF.Infrastructure/
COPY backend/src/NoCTF.PluginBase/NoCTF.PluginBase.csproj src/NoCTF.PluginBase/
COPY backend/src/NoCTF.Plugins.AWD/NoCTF.Plugins.AWD.csproj src/NoCTF.Plugins.AWD/
COPY backend/src/NoCTF.Plugins.AWDP/NoCTF.Plugins.AWDP.csproj src/NoCTF.Plugins.AWDP/
COPY backend/src/NoCTF.Plugins.CTF/NoCTF.Plugins.CTF.csproj src/NoCTF.Plugins.CTF/
COPY backend/src/NoCTF.Plugins.KoH/NoCTF.Plugins.KoH.csproj src/NoCTF.Plugins.KoH/
COPY backend/src/NoCTF.Plugins.Penetration/NoCTF.Plugins.Penetration.csproj src/NoCTF.Plugins.Penetration/
COPY backend/src/NoCTF.Runner.Client/NoCTF.Runner.Client.csproj src/NoCTF.Runner.Client/
COPY backend/src/NoCTF.Runner/NoCTF.Runner.csproj src/NoCTF.Runner/
COPY backend/src/NoCTF.Worker/NoCTF.Worker.csproj src/NoCTF.Worker/

RUN dotnet restore src/NoCTF.API/NoCTF.API.csproj
RUN dotnet restore src/NoCTF.Worker/NoCTF.Worker.csproj
RUN dotnet restore src/NoCTF.Runner/NoCTF.Runner.csproj
RUN dotnet restore src/NoCTF.Plugins.CTF/NoCTF.Plugins.CTF.csproj
RUN dotnet restore src/NoCTF.Plugins.AWD/NoCTF.Plugins.AWD.csproj
RUN dotnet restore src/NoCTF.Plugins.AWDP/NoCTF.Plugins.AWDP.csproj
RUN dotnet restore src/NoCTF.Plugins.KoH/NoCTF.Plugins.KoH.csproj
RUN dotnet restore src/NoCTF.Plugins.Penetration/NoCTF.Plugins.Penetration.csproj

# Copy all source and publish
COPY backend/ .
RUN mkdir -p /app/api/plugins /app/worker /app/runner && \
    dotnet publish src/NoCTF.API/NoCTF.API.csproj -c Release -o /app/api --no-restore /p:BuildSpaOnPublish=false && \
    dotnet publish src/NoCTF.Worker/NoCTF.Worker.csproj -c Release -o /app/worker --no-restore && \
    dotnet publish src/NoCTF.Runner/NoCTF.Runner.csproj -c Release -o /app/runner --no-restore && \
    dotnet publish src/NoCTF.Plugins.CTF/NoCTF.Plugins.CTF.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.AWD/NoCTF.Plugins.AWD.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.AWDP/NoCTF.Plugins.AWDP.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.KoH/NoCTF.Plugins.KoH.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.Penetration/NoCTF.Plugins.Penetration.csproj -c Release -o /app/api/plugins --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0.28 AS api
WORKDIR /app

COPY --from=build /app/api .
COPY --from=frontend-build /src/frontend/dist ./wwwroot

EXPOSE 8080

ENTRYPOINT ["dotnet", "NoCTF.API.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0.28 AS worker
WORKDIR /app

COPY --from=build /app/worker .

ENTRYPOINT ["dotnet", "NoCTF.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0.28 AS runner
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl gnupg && \
    install -m 0755 -d /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc && \
    chmod a+r /etc/apt/keyrings/docker.asc && \
    . /etc/os-release && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian ${VERSION_CODENAME} stable" > /etc/apt/sources.list.d/docker.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin && \
    apt-get purge -y --auto-remove curl gnupg && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/runner .

EXPOSE 8080

ENTRYPOINT ["dotnet", "NoCTF.Runner.dll"]
