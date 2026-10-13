FROM docker.m.daocloud.io/oven/bun:1.3.14@sha256:e10577f0db68676a7024391c6e5cb4b879ebd17188ab750cf10024a6d700e5c4 AS frontend-build
WORKDIR /src/backend/src/NoCTF.API/ClientApp
COPY backend/src/NoCTF.API/ClientApp/package.json backend/src/NoCTF.API/ClientApp/bun.lock ./
RUN bun install --frozen-lockfile --ignore-scripts
COPY backend/src/NoCTF.API/ClientApp/ ./
COPY backend/src/NoCTF.API/Localization/Catalogs/ /src/backend/src/NoCTF.API/Localization/Catalogs/
COPY backend/scripts/Validate-Translations.ts /src/backend/scripts/Validate-Translations.ts
RUN bun run postinstall
RUN bun --bun run generate

FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src
COPY backend/ ./backend/
WORKDIR /src/backend
RUN dotnet restore src/NoCTF.Host/NoCTF.Host.csproj
RUN dotnet build src/NoCTF.Host/NoCTF.Host.csproj -c Release --no-restore -p:NoCtfGenerateWolverineHandlers=true
RUN export ASPNETCORE_ENVIRONMENT=Production \
    Authentication__SigningKey="$(head -c 32 /dev/urandom | base64)" \
    ConnectionStrings__Redis=localhost:6379 \
    ConnectionStrings__Nats=nats://localhost:4222 \
    ConnectionStrings__PostgreSql='Host=localhost;Database=noctf_codegen;Username=postgres;Password=unused' \
    RunnerScoring__SigningKey="$(head -c 32 /dev/urandom | base64)" \
    RunnerScoring__CallbackBaseUrl=http://localhost:5080 \
    Webhooks__PublicBaseUrl=https://noctf.invalid \
    Runner__Id=codegen-runner \
    Runner__Pool=codegen \
    Runner__Provider=Docker \
    && dotnet run --no-build -c Release --project src/NoCTF.Host/NoCTF.Host.csproj -- codegen delete \
    && dotnet run --no-build -c Release --project src/NoCTF.Host/NoCTF.Host.csproj -- codegen write \
    > /tmp/noctf-codegen.log 2>&1 \
    && cat /tmp/noctf-codegen.log \
    && ! grep -q '^ERROR:' /tmp/noctf-codegen.log
RUN dotnet publish src/NoCTF.Host/NoCTF.Host.csproj -c Release -o /app/host /p:BuildSpaOnPublish=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS runtime
RUN sed -i 's|http://archive.ubuntu.com/ubuntu|https://archive.ubuntu.com/ubuntu|g; s|http://security.ubuntu.com/ubuntu|https://security.ubuntu.com/ubuntu|g' \
        /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --chmod=0755 backend/docker/healthcheck.sh /usr/local/bin/noctf-healthcheck
HEALTHCHECK --interval=10s --timeout=3s --start-period=120s --retries=12 CMD ["/usr/local/bin/noctf-healthcheck"]

FROM runtime AS host
WORKDIR /app
COPY LICENSE ./LICENSE
COPY LICENSES ./LICENSES
COPY --from=build /app/host .
COPY --from=frontend-build /src/backend/src/NoCTF.API/ClientApp/.output/public ./wwwroot
RUN mkdir -p /app/uploads && chown -R 1654:1654 /app/uploads
# The combined Host includes Runner and accesses the mounted Docker socket.
USER 0
ENTRYPOINT ["dotnet", "NoCTF.Host.dll"]
