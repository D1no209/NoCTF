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

RUN dotnet restore src/NoCTF.API/NoCTF.API.csproj

# Copy all source and publish
COPY . .
RUN dotnet publish src/NoCTF.API/NoCTF.API.csproj -c Release -o /app/publish --no-restore

# Copy plugin DLLs into publish output
RUN mkdir -p /app/publish/plugins && \
    cp /src/src/NoCTF.Plugins.CTF/bin/Release/net8.0/NoCTF.Plugins.CTF.dll /app/publish/plugins/ && \
    cp /src/src/NoCTF.Plugins.AWD/bin/Release/net8.0/NoCTF.Plugins.AWD.dll /app/publish/plugins/ && \
    cp /src/src/NoCTF.Plugins.AWDP/bin/Release/net8.0/NoCTF.Plugins.AWDP.dll /app/publish/plugins/ && \
    cp /src/src/NoCTF.Plugins.KoH/bin/Release/net8.0/NoCTF.Plugins.KoH.dll /app/publish/plugins/

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "NoCTF.API.dll"]
