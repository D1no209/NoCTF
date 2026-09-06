FROM docker.m.daocloud.io/oven/bun:1.3.14@sha256:e10577f0db68676a7024391c6e5cb4b879ebd17188ab750cf10024a6d700e5c4 AS frontend-build
WORKDIR /src/ClientApp
COPY backend/src/NoCTF.API/ClientApp/package.json backend/src/NoCTF.API/ClientApp/bun.lock ./
RUN bun install --frozen-lockfile
COPY backend/src/NoCTF.API/ClientApp/ ./
RUN bunx nuxt prepare
RUN bun --bun run generate

FROM docker.m.daocloud.io/docker:28.5.1-cli@sha256:9190b0613792e658a7783cf14b2d5ace5941bb68ede7276922ea36ee457d76ad AS docker-cli

FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS kompose-cli
ARG KOMPOSE_VERSION=1.38.0
ARG TARGETARCH
COPY backend/docker-assets/ /tmp/kompose-assets/
RUN case "${TARGETARCH}" in \
      amd64) KOMPOSE_SHA256=65a6a720605bead3964e8b22d423a0763de451a236fe03de902e366cf3d9c147 ;; \
      arm64) KOMPOSE_SHA256=24faf6212ec34d325e4724118098d34b9dd321cda2d9ca22efa0f04cdb909894 ;; \
      arm) KOMPOSE_SHA256=53119ee98adfe6fe430704876f4ad80f571733aaba4701eac1a17012efbb71c8 ;; \
      *) echo "Unsupported Kompose architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && KOMPOSE_ASSET="/tmp/kompose-assets/kompose-linux-${TARGETARCH}" \
    && if [ -s "${KOMPOSE_ASSET}" ]; then \
      install -m 0755 "${KOMPOSE_ASSET}" /usr/local/bin/kompose; \
    else \
      curl --fail --location --show-error \
        --connect-timeout 15 --max-time 300 \
        --retry 8 --retry-delay 2 --retry-all-errors \
        --continue-at - --speed-limit 1024 --speed-time 30 \
        --output /usr/local/bin/kompose \
        "https://github.com/kubernetes/kompose/releases/download/v${KOMPOSE_VERSION}/kompose-linux-${TARGETARCH}"; \
    fi \
    && echo "${KOMPOSE_SHA256}  /usr/local/bin/kompose" | sha256sum -c - \
    && chmod 0755 /usr/local/bin/kompose

FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS runtime
RUN sed -i 's|http://archive.ubuntu.com/ubuntu|https://archive.ubuntu.com/ubuntu|g; s|http://security.ubuntu.com/ubuntu|https://security.ubuntu.com/ubuntu|g' \
        /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --chmod=0755 backend/docker/healthcheck.sh /usr/local/bin/noctf-healthcheck
HEALTHCHECK --interval=10s --timeout=3s --start-period=120s --retries=12 CMD ["/usr/local/bin/noctf-healthcheck"]

FROM runtime AS api
WORKDIR /app
COPY --from=build /app/api .
COPY --from=frontend-build /src/ClientApp/.output/public ./wwwroot
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
USER 1654
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
# Docker socket access already grants host-level authority; no host socket chmod/group patching.
USER 0
ENTRYPOINT ["dotnet", "NoCTF.Runner.dll"]

FROM runtime AS host
WORKDIR /app
COPY --from=build /app/host .
COPY --from=frontend-build /src/ClientApp/.output/public ./wwwroot
COPY --from=docker-cli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=docker-cli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
COPY --from=kompose-cli /usr/local/bin/kompose /usr/local/bin/kompose
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
# The combined Host includes Runner and accesses the mounted Docker socket.
USER 0
ENTRYPOINT ["dotnet", "NoCTF.Host.dll"]
