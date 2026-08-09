FROM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/ClientApp
COPY backend/src/NoCTF.API/ClientApp/package.json backend/src/NoCTF.API/ClientApp/bun.lock ./
RUN bun install --frozen-lockfile --ignore-scripts
COPY backend/src/NoCTF.API/ClientApp/ ./
RUN bun --bun run generate

FROM docker:28-cli AS docker-cli

FROM alpine:3.22 AS kompose-cli
ARG TARGETARCH
RUN wget -q -O /usr/local/bin/kompose \
    "https://github.com/kubernetes/kompose/releases/download/v1.38.0/kompose-linux-${TARGETARCH}" \
    && chmod 0755 /usr/local/bin/kompose

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./backend/
WORKDIR /src/backend
RUN dotnet restore src/NoCTF.API/NoCTF.API.csproj
RUN dotnet restore src/NoCTF.Worker/NoCTF.Worker.csproj
RUN dotnet restore src/NoCTF.Runner/NoCTF.Runner.csproj
RUN dotnet restore src/NoCTF.Host/NoCTF.Host.csproj
RUN dotnet publish src/NoCTF.API/NoCTF.API.csproj -c Release -o /app/api /p:BuildSpaOnPublish=false
RUN dotnet publish src/NoCTF.Worker/NoCTF.Worker.csproj -c Release -o /app/worker
RUN dotnet publish src/NoCTF.Runner/NoCTF.Runner.csproj -c Release -o /app/runner
RUN dotnet publish src/NoCTF.Host/NoCTF.Host.csproj -c Release -o /app/host /p:BuildSpaOnPublish=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

FROM runtime AS api
WORKDIR /app
COPY --from=build /app/api .
COPY --from=frontend-build /src/ClientApp/.output/public ./wwwroot
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "NoCTF.API.dll"]

FROM runtime AS worker
WORKDIR /app
COPY --from=build /app/worker .
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
USER 1654
ENTRYPOINT ["dotnet", "NoCTF.Worker.dll"]

FROM runtime AS runner
WORKDIR /app
COPY --from=build /app/runner .
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
COPY --from=kompose-cli /usr/local/bin/kompose /usr/local/bin/kompose
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "NoCTF.Runner.dll"]

FROM runtime AS host
WORKDIR /app
COPY --from=build /app/host .
COPY --from=frontend-build /src/ClientApp/.output/public ./wwwroot
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
COPY --from=kompose-cli /usr/local/bin/kompose /usr/local/bin/kompose
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "NoCTF.Host.dll"]
