# NoCTF ClientApp

This directory contains the Bun-managed Nuxt 4 SPA hosted by `NoCTF.API`.
It is currently a minimal skeleton: no UI framework, no pages, no API client.

- `dotnet run --project ../NoCTF.API.csproj` starts the API and lets ASP.NET Core
  SpaProxy launch the Nuxt development server at `http://127.0.0.1:3000`.
- `bun run dev` starts Nuxt directly when backend proxying is not needed.
- `dotnet publish ../NoCTF.API.csproj -c Release` runs `bun install
  --frozen-lockfile`, generates the static SPA, and places it under the API's
  published `wwwroot` directory.

Nuxt is configured with client-side rendering (`ssr: false`) because ASP.NET
Core remains the only production web process. The dev server proxies `/api`,
`/hubs`, and `/health` to the backend at `http://localhost:5080`.
