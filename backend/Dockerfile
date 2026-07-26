FROM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY frontend/ ./
RUN bun run build

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
RUN dotnet restore src/NoCTF.Runner/NoCTF.Runner.csproj
RUN dotnet publish src/NoCTF.API/NoCTF.API.csproj -c Release -o /app/api /p:BuildSpaOnPublish=false
RUN dotnet publish src/NoCTF.Runner/NoCTF.Runner.csproj -c Release -o /app/runner

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/api .
COPY --from=frontend-build /src/frontend/dist ./wwwroot
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
USER 1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "NoCTF.API.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runner
WORKDIR /app
COPY --from=build /app/runner .
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
COPY --from=kompose-cli /usr/local/bin/kompose /usr/local/bin/kompose
EXPOSE 8080
ENTRYPOINT ["dotnet", "NoCTF.Runner.dll"]
