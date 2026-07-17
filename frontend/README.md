# Vue 3 + TypeScript + Vite

This template should help get you started developing with Vue 3 and TypeScript in Vite. The template uses Vue 3 `<script setup>` SFCs, check out the [script setup docs](https://v3.vuejs.org/api/sfc-script-setup.html#sfc-script-setup) to learn more.

Learn more about the recommended Project Setup and IDE Support in the [Vue Docs TypeScript Guide](https://vuejs.org/guide/typescript/overview.html#project-setup).

## Development API and mock data

`bun run dev` proxies `/api` and `/hubs` to the real local backend by default. When the backend is not running, the dev session detects the outage (network failure or proxy 5xx, confirmed by an `/api/health` probe) and automatically switches to the mock route table in `src/mocks/mock-data.json` — no configuration needed. A `[noctf-mock]` console message announces the switch, and requests without a registered mock route return a diagnosable 404 with a console warning naming the missing route.

Two explicit overrides remain:

```powershell
$env:VITE_ENABLE_MOCKS = 'true'   # always serve registered mock routes (may mix with a live backend)
$env:VITE_ENABLE_MOCKS = 'false'  # never serve mocks, even when the backend is down
bun run dev
```

Mock mode only exists in development builds. The mock login JWT is intentionally not accepted by the real backend: once mock mode is active, reload the page after starting the backend to resume full-stack testing.
