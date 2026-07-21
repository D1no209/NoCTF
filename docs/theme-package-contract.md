# Theme Package Contract

This manual is the binding contract for frontend theme (UI) packages in NoCTF.
It exists so that **V1 and V2 are pure rendering packages**: they paint pixels
and forward user intent, but they do not own page functionality. All page
functionality lives in shared, theme-agnostic **feature modules**.

> Hard rules (non-negotiable):
> 1. **Pages and page behavior follow the existing V1 implementation.** V1 is
>    the functional reference. If behavior must change, it changes in the
>    feature module with V1 semantics, and every theme re-renders it.
> 2. **A refactor must never break a working page.** Move code verbatim first,
>    clean up second, and let `vue-tsc` + `vite build` verify every step.

## 1. Layer model

```
frontend/src/
  api/                  HTTP/SDK wrappers + queryKeys. The only place API contracts live.
  stores/               Session/auth state (pinia).
  composables/          Platform services: useSignalR, useThemePackages.
  features/             Page feature modules (view models). One module per route.
    shared/             Cross-page domain helpers (number coercion, etc.).
    <domain>/           auth, home, competitions, teams, dashboards, screen, admin.
  themes/               Token packs: manifest, presets, storage, import/export.
  ui-v1/                UI package "v1" (default package; shadcn-vue style; i18n).
    V1Application.vue   Chrome: RouterView + PixelBlast + TargetCursor + Toaster.
    views/              Thin route wrappers (3-line files).
    components/         Page-level workspaces + presentational components + ui/ primitives.
  ui-v2/                UI package "v2" (neumorphic command style; hardcoded English).
    V2Application.vue   Chrome: route-name -> page mapping + shells.
    layouts/ pages/ components/ primitives/ composables/
  ui-package-registry.ts
  App.vue               Package switch only. No page logic, no chrome.
```

### Import rules

| Layer | May import | Must NOT import |
|---|---|---|
| `api/` | the generated SDK | anything outside `api/` |
| `composables/` | `api/`, `stores/`, frameworks | theme packages, `features/` |
| `features/` | `api/`, `stores/`, `composables/`, `vue-query`, `vue-router`, `vue` | `ui-v1/`, `ui-v2/`, `themes/`, any `.vue` file |
| theme package (`ui-v1/`, `ui-v2/`) | own package files, `@/features` (**page-level components only**), `@/composables` (platform services), `@/stores/auth` (navigation chrome only), icon libraries, `import type` from `@/features` | runtime calls into `@/api/*`, `@tanstack/vue-query`, the **other** theme package |
| `themes/` | `themes/` itself | components, `features/` |

Additional component-level rules inside a theme package:

- **One page-level binding component per route.** This is the only component
  that may call a feature hook. In V1 that component is the route's
  `*Workspace.vue`; in V2 it is the route's `V2*Page.vue`. Everything below it
  is a pure presentational component.
- **Pure presentational components** receive props and emit events. They may
  hold presentational state (search text, sort key, page index, hover, open
  dialogs) but must not import `@/features`, `@/api`, `vue-query`, or
  `vue-router`, and must not touch stores.
- **Layouts and navigation chrome** (nav bars, admin nav, screen headers) may
  read the current route and render links, and may call `auth` store actions
  such as logout. They may not issue domain queries or mutations.
- A theme package must never reach into another theme package, not even for
  types. If two packages need the same type, it belongs in `features/` (domain
  types) or in the platform layer.

## 2. Feature module contract

A feature module is the single source of truth for one route's functionality.

**File:** `src/features/<domain>/use<Name>Page.ts`
**Export:** one composable returning the page view model, plus the domain DTO
types the themes need (themes import these types from the feature, never from
`@/api`).

A feature module **owns**:

- All server interaction: query keys, query functions, mutation functions,
  invalidation sets, polling intervals, retry policy, SignalR subscription
  lifecycle (started/stopped inside the composable).
- Route awareness: `route.params`, `route.query`, and every navigation intent
  (`router.push`/`replace` after create/delete, section switching).
- All domain form state: field refs, hydration watchers that populate forms
  from query data, dirty/coercion rules, and payload builders.
- Derived domain state: filtered/merged server data, `canSave`-style guards,
  enum mappings, fallback chains.

A feature module **must not**:

- Render anything, import `.vue` files, or know which theme is active.
- Show toasts, banners, or any UI feedback, and must not contain user-facing
  copy. Feedback is presentation: the module exposes mutation objects and the
  theme attaches per-call callbacks.

### Feedback pattern

```ts
// features/admin/useAdminQqBotPage.ts
const saveSettingsMutation = useMutation({
  mutationFn: () => adminApi.updateQqBotSettings(payload()),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-qqbot'] }),
})
return { saveSettingsMutation /* ... */ }

// ui-v1 (toast + i18n)
saveSettingsMutation.mutate(undefined, {
  onSuccess: () => toast.success(t('admin.qqBot.settingsSaved')),
  onError: () => toast.error(t('admin.qqBot.settingsSaveFailed')),
})

// ui-v2 (inline banner + English)
saveSettingsMutation.mutate(undefined, {
  onSuccess: () => reportSuccess('QQ bot settings saved.'),
  onError: error => reportError(error, 'Unable to save the QQ bot settings.'),
})
```

Domain side effects that are not feedback (resetting a create form after a
successful submit, clearing a selection after delete) belong in the module's
own `onSuccess`, not in the theme callback.

### Numeric form fields

Command-style inputs in V2 emit strings while V1 binds numbers with
`v-model.number`. The canonical representation inside a feature module is a
**string per numeric field**; payload builders convert at the boundary with
the shared helpers from `features/shared/number.ts`:

- `numberOrDefault(value, fallback)` — `''`/null/undefined/NaN → fallback.
- `optionalNumber(value)` — `''`/null/undefined → `undefined`.
- `numberField(value)` — preserves V1's `v-model.number` contract for settings
  payloads (typed `number`, keeps `''` at runtime when cleared).
- `firstNumberString(...candidates)` — hydration fallback chain that skips
  undefined/null/`''` and returns `String(value)`.

Themes must not do arithmetic on form strings; domain comparisons
(`min < max`, `end > start`) are computed inside the feature module.

### Outcome actions and failure tokens

Async flows whose result decides the copy (login, registration, email
verification, AWD flag/patch submission, the challenge console) return a
discriminated **outcome object** instead of a mutation:

```ts
type ChallengeFlagOutcome =
  | { kind: 'correct', alreadySolved?: boolean }
  | { kind: 'incorrect', message?: string, reason?: string }
  | { kind: 'failed', message?: string }
  | { kind: 'skipped' }   // guard failed (empty input, no permission); themes stay silent
```

`'skipped'` mirrors V1's silent early return. Errors that must stay visible on
the page (rather than in a toast) are exposed as **failure-kind tokens**
(`submitFailureKind: 'attempts_exhausted' | 'instance_required' | ...`); each
theme maps tokens to its own copy in a computed.

### Parameterized features

Component-level features take an **options bag of getters** so the module never
touches props directly:

```ts
useChallengeConsole({ competitionId: () => props.competitionId, open: () => props.open, ... })
useAwdDashboardPage(() => props.gameModeType)
```

Tri-state booleans read as `xxx !== false` / `xxx === false` (undefined keeps
the V1 prop-default semantics). V2's console passes `canCreateInstance: () =>
false` to suppress instance polling.

### Raw passthrough

When a V1 template addresses members of a query/mutation/SignalR object
(`query.isPending.value`, `signalR.isConnected.value`), the feature returns the
raw object unchanged so the template stays byte-identical. New code should
prefer destructured top-level refs.

### Manual fetch state machines

Pages V1 never implemented with vue-query (competitions list, admin health)
keep V1's imperative `reactive({ ... })` + explicit load-function state machine
inside the feature. Do not rewrite them as vue-query.

### DTO policy

- Prefer the generated SDK DTOs; themes patch optional fields with display
  mappings (defaults, trims, filters).
- When a V1 local interface carries fields the SDK lacks (e.g. `firstBloods`)
  or matches a child component's prop type verbatim, the feature adopts the
  **V1 interface verbatim** (asserted at the API boundary). V1 then needs zero
  mapping; V2 maps as usual.

## 3. UI package contract

A UI package is a self-contained rendering implementation of every route.

**Directory layout** (package id `<x>`): `src/ui-<x>/` with a package entry
component `<X>Application.vue`, an `index.ts` barrel exporting
`{ routeNames, loadApplication }`, and any of `layouts/`, `pages/`,
`components/`, `primitives/`, `composables/` (presentational hooks only).

**Styling.** All styles are scoped to the package. A package prefixes its CSS
custom properties (`--v1-*`, `--v2-*`) and must render correctly under any
token pack from `themes/`. Token packs are applied to `documentElement` by the
platform; a package chooses which manifest tokens it honors and keeps the rest
behind its own scoped defaults. No borders/gradients/uppercase rules and the
like are package-internal decisions — the platform does not impose a look.

**Copy.** A package picks one copy strategy and stays consistent: V1 uses
`vue-i18n` keys; V2 uses hardcoded English. Feature modules never supply copy.

**Route coverage.** A package lists the route names it renders. Routes it does
not list fall back to the default package (`v1`). Full coverage of the 30
registered routes is the definition of "aligned with V1".

## 4. Registering a new UI package

1. Create `src/ui-<id>/` per section 3.
2. Extend `UiPackageId` and `isUiPackageId` in `src/themes/theme-package.ts`.
3. Register the package in `src/ui-package-registry.ts`:
   `{ id, routeNames, loadApplication }` (`routeNames: null` means "all").
4. Add at least one built-in preset with `uiPackage: '<id>'` in
   `src/themes/presets.ts` (token manifest must satisfy
   `assertThemeTokenCoverage`).
5. Keep `App.vue` untouched — it only reads the registry.

## 5. Adding or changing a page

1. Implement/extend the feature module first, with V1 semantics.
2. Wire the V1 binding component (the functional reference UI).
3. Wire the V2 binding component to the same view model.
4. Register the route in `src/router/index.ts` and in both packages' route
   coverage lists.
5. Verify: `bun run build` (vue-tsc + vite) and click through both themes.

V1 template edits are limited to two kinds: renaming a binding
(`auth.isAuthenticated` → `isAuthenticated`) and rebinding an event to a
feature action. Anything more means the feature's return shape is wrong — fix
the feature, not the template.

Code-review questions that must always be answered "no":

- Does any file under `ui-v1/` or `ui-v2/` import `@/api/*` at runtime,
  `@tanstack/vue-query`, or the other theme package?
- Does any presentational component import `@/features`, `vue-router`, or a
  store?
- Does any feature module import a `.vue` file or contain user-facing copy?
- Did any payload, query key, invalidation set, enum, or fallback chain
  diverge from V1?

## 6. Route → feature map (compliance inventory)

| Route | Feature module | V1 binding | V2 binding |
|---|---|---|---|
| home | `features/home/useHomePage.ts` | `home/HomeWorkspace.vue` | `pages/V2HomePage.vue` |
| login | `features/auth/useLoginPage.ts` | `auth/LoginWorkspace.vue` | `pages/V2LoginPage.vue` |
| register | `features/auth/useRegisterPage.ts` | `auth/RegisterWorkspace.vue` | `pages/V2RegisterPage.vue` |
| verify-email | `features/auth/useVerifyEmailPage.ts` | `auth/VerifyEmailWorkspace.vue` | `pages/V2VerifyEmailPage.vue` |
| competitions | `features/competitions/useCompetitionsPage.ts` | `competitions/CompetitionsWorkspace.vue` | `pages/V2CompetitionsPage.vue` |
| competition-detail | `features/competitions/useCompetitionGateway.ts` + `useCompetitionDetailPage.ts` + `useCompetitionLeaderboard.ts` | `views/CompetitionGatewayView.vue` + `competition-detail/CompetitionDetailWorkspace.vue` | `pages/V2CompetitionDetailPage.vue` |
| competition-register | `features/competitions/useCompetitionRegistrationPage.ts` | `competition-registration/CompetitionRegistrationWorkspace.vue` | `pages/V2CompetitionRegistrationPage.vue` |
| teams | `features/teams/useTeamsPage.ts` | `teams/TeamsPageWorkspace.vue` | `pages/V2TeamsPage.vue` |
| awd-dashboard | `features/competitions/useAwdDashboardPage.ts` | `awd/AwdDashboardWorkspace.vue` | `pages/V2AwdDashboardPage.vue` |
| koh-dashboard | `features/competitions/useKohDashboardPage.ts` | `koh/KohDashboardWorkspace.vue` | `pages/V2KohDashboardPage.vue` |
| penetration-dashboard | `features/competitions/usePenetrationDashboardPage.ts` | `penetration/PenetrationWorkspace.vue` | `pages/V2PenetrationDashboardPage.vue` |
| awdp-screen | `features/screen/useAwdpScreenData.ts` | `views/AwdpScreenView.vue` | `pages/V2AwdpScreenPage.vue` |
| admin-users | `features/admin/useAdminUsersPage.ts` | `admin/users/AdminUsersWorkspace.vue` | `pages/V2AdminUsersPage.vue` |
| admin-teams | `features/admin/useAdminTeamsPage.ts` | `admin/teams/AdminTeamsWorkspace.vue` | `pages/V2AdminTeamsPage.vue` |
| admin-competitions | `features/admin/useAdminCompetitionsPage.ts` | `admin/competitions/AdminCompetitionsWorkspace.vue` | `pages/V2AdminCompetitionsPage.vue` |
| admin-competition-detail | `features/admin/useAdminCompetitionDetailPage.ts` | `admin/competition-detail/AdminCompetitionDetailWorkspace.vue` | `pages/V2AdminCompetitionDetailPage.vue` |
| admin-competition-operations | `features/admin/useAdminCompetitionOperationsPage.ts` | `admin/competition-operations/AdminCompetitionOperationsWorkspace.vue` | `pages/V2AdminCompetitionOperationsPage.vue` |
| admin-collaborators | `features/admin/useAdminCollaboratorsPage.ts` | `admin/collaborators/AdminCollaboratorsWorkspace.vue` | `pages/V2AdminCollaboratorsPage.vue` |
| admin-challenges | `features/admin/useAdminChallengesPage.ts` | `admin/challenges/AdminChallengesWorkspace.vue` | `pages/V2AdminChallengesPage.vue` |
| admin-challenge-create | `features/admin/useAdminChallengeCreatePage.ts` | `admin/challenges/AdminChallengeCreateWorkspace.vue` | `pages/V2AdminChallengeCreatePage.vue` |
| admin-containers | `features/admin/useAdminContainersPage.ts` | `admin/containers/AdminContainersWorkspace.vue` | `pages/V2AdminContainersPage.vue` |
| admin-plugins | `features/admin/useAdminPluginsPage.ts` | `admin/plugins/AdminPluginsWorkspace.vue` | `pages/V2AdminPluginsPage.vue` |
| admin-infrastructure | `features/admin/useAdminInfrastructurePage.ts` | `admin/infrastructure/AdminInfrastructureWorkspace.vue` | `pages/V2AdminInfrastructurePage.vue` |
| admin-qqbot | `features/admin/useAdminQqBotPage.ts` | `admin/qqbot/AdminQqBotWorkspace.vue` | `pages/V2AdminQqBotPage.vue` |
| admin-email-verification | `features/admin/useAdminEmailVerificationPage.ts` | `admin/email-verification/AdminEmailVerificationWorkspace.vue` | `pages/V2AdminEmailVerificationPage.vue` |
| admin-audit-logs | `features/admin/useAdminAuditLogsPage.ts` | `admin/audit-logs/AdminAuditLogsWorkspace.vue` | `pages/V2AdminAuditLogsPage.vue` |
| admin-health | `features/admin/useAdminHealthPage.ts` | `admin/health/AdminHealthWorkspace.vue` | `pages/V2AdminHealthPage.vue` |
| admin-logs | `features/admin/useAdminLogsPage.ts` | `admin/logs/AdminLogsWorkspace.vue` | `pages/V2AdminLogsPage.vue` |
| admin-theme-packs | platform service `useThemePackages` | `views/admin/AdminThemePacksView.vue` | `pages/V2ThemePackagesPage.vue` |
| not-found | — (static) | `views/NotFoundView.vue` | `pages/V2NotFoundPage.vue` |

Shared challenge-template editing (used by admin-challenges,
admin-challenge-create and admin-competition-detail) lives in
`features/admin/challengeTemplate.ts`; each theme renders it with its
own form component (V1 `ChallengeTemplateForm.vue`, V2
`CommandChallengeTemplateForm.vue`).

Cross-page interactive components have their own parameterized features:
`features/game/useChallengeConsole.ts` (V1 `ChallengeModal.vue`, V2
`CommandChallengeConsole.vue`) and `features/game/useScoreboard.ts` (V1
`ScoreboardView.vue`). Chrome and notification state live in
`features/chrome/useChromeSession.ts`, `features/chrome/useGameLayoutScore.ts`
and `features/notifications/useNotificationCenter.ts`, consumed by both
packages' navigation shells.
