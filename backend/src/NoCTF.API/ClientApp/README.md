# NoCTF ClientApp

This directory contains the Bun-managed Nuxt 4 SPA hosted by `NoCTF.API`.
It uses shadcn-vue primitives, a generated API SDK and separate feature and rendering layers.

See [ARCHITECTURE.md](ARCHITECTURE.md) for boundaries and the completed architecture audit.

- `app/pages/`, `app/layouts/`, `app/app.vue`: route metadata and feature entry points.
- `app/features/`: feature controllers, state, commands and composition.
- `app/components/views/`: rendering, bindings and command forwarding.
- `app/components/ui/`: the only shared UI primitive implementations.
- `app/locales/`: stable message keys with Chinese and English resources.

Run `bun run audit:architecture`, `bun test`, `bun run typecheck` and
`bun run generate` before delivering an architecture change.

The approved UI contract is documented in the repository's `DESIGN.md` and
[UI-REDESIGN-PROPOSAL.md](UI-REDESIGN-PROPOSAL.md). In development,
`/__ui-check` previews themes, cards, form controls and notifications without
submitting business data. Production builds exclude that route.

- `dotnet run --project ../NoCTF.API.csproj` starts the API and lets ASP.NET Core
  SpaProxy launch the Nuxt development server at `http://127.0.0.1:3000`.
- `bun run dev` starts Nuxt directly when backend proxying is not needed.
- `dotnet publish ../NoCTF.API.csproj -c Release` runs `bun install
  --frozen-lockfile`, generates the static SPA, and places it under the API's
  published `wwwroot` directory.

Nuxt is configured with client-side rendering (`ssr: false`) because ASP.NET
Core remains the only production web process. The dev server proxies `/api`,
`/hubs`, and `/health` to the backend at `http://localhost:5080`.
