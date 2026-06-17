# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for layer caching
COPY NoCTF.slnx ./
COPY src/NoCTF.API/NoCTF.API.csproj src/NoCTF.API/
COPY src/NoCTF.Application/NoCTF.Application.csproj src/NoCTF.Application/
COPY src/NoCTF.Container.Docker/NoCTF.Container.Docker.csproj src/NoCTF.Container.Docker/
COPY src/NoCTF.Container.K8s/NoCTF.Container.K8s.csproj src/NoCTF.Container.K8s/
COPY src/NoCTF.Core/NoCTF.Core.csproj src/NoCTF.Core/
COPY src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj src/NoCTF.Infrastructure/
COPY src/NoCTF.PluginBase/NoCTF.PluginBase.csproj src/NoCTF.PluginBase/
COPY src/NoCTF.Plugins.AWD/NoCTF.Plugins.AWD.csproj src/NoCTF.Plugins.AWD/
COPY src/NoCTF.Plugins.AWDP/NoCTF.Plugins.AWDP.csproj src/NoCTF.Plugins.AWDP/
COPY src/NoCTF.Plugins.CTF/NoCTF.Plugins.CTF.csproj src/NoCTF.Plugins.CTF/
COPY src/NoCTF.Plugins.KoH/NoCTF.Plugins.KoH.csproj src/NoCTF.Plugins.KoH/
COPY src/NoCTF.Runner.Client/NoCTF.Runner.Client.csproj src/NoCTF.Runner.Client/
COPY src/NoCTF.Runner/NoCTF.Runner.csproj src/NoCTF.Runner/
COPY src/NoCTF.Worker/NoCTF.Worker.csproj src/NoCTF.Worker/

RUN dotnet restore src/NoCTF.API/NoCTF.API.csproj
RUN dotnet restore src/NoCTF.Worker/NoCTF.Worker.csproj
RUN dotnet restore src/NoCTF.Runner/NoCTF.Runner.csproj

# Copy all source and publish
COPY . .
RUN mkdir -p /app/api/plugins /app/worker /app/runner && \
    dotnet publish src/NoCTF.API/NoCTF.API.csproj -c Release -o /app/api --no-restore && \
    dotnet publish src/NoCTF.Worker/NoCTF.Worker.csproj -c Release -o /app/worker --no-restore && \
    dotnet publish src/NoCTF.Runner/NoCTF.Runner.csproj -c Release -o /app/runner --no-restore && \
    dotnet publish src/NoCTF.Plugins.CTF/NoCTF.Plugins.CTF.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.AWD/NoCTF.Plugins.AWD.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.AWDP/NoCTF.Plugins.AWDP.csproj -c Release -o /app/api/plugins --no-restore && \
    dotnet publish src/NoCTF.Plugins.KoH/NoCTF.Plugins.KoH.csproj -c Release -o /app/api/plugins --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS api
WORKDIR /app

COPY --from=build /app/api .

EXPOSE 8080

ENTRYPOINT ["dotnet", "NoCTF.API.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS worker
WORKDIR /app

COPY --from=build /app/worker .

ENTRYPOINT ["dotnet", "NoCTF.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runner
WORKDIR /app

COPY --from=build /app/runner .

EXPOSE 8080

ENTRYPOINT ["dotnet", "NoCTF.Runner.dll"]
