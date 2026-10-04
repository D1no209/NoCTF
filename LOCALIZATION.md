# Localization and Crowdin

English (`en`) is the source language. The initial target is Simplified Chinese
(`zh-CN`). UI preferences retain browser detection, local persistence and the
existing language-switch animation. HTTP requests use `Accept-Language`; the API
matches English and Chinese language families and defaults to English.

## Adding text

Use a semantic, stable key such as `auth.login.invalidCredentials`,
`runtime.extend.tooEarly` or `common.action.save`. Do not use source sentences,
sentence slugs, generic `ui.*` keys or numbered variants. A wording change keeps
the key; a meaning change gets a new key. Place frontend text in its feature JSON
catalog under `backend/src/NoCTF.API/ClientApp/app/locales/catalogs/en`.
`t` and `$t` check keys against this English catalog.

HTTP messages are owned by `backend/src/NoCTF.API/Localization/Catalogs` and
`ApiMessageRegistry`: map typed application/protocol failures to a closed message
identity at the API boundary. Do not identify errors from their English text.
Use named interpolation (`{field}`, `{maximumBytes}`), keeping the parameter set
identical in every nonempty translation. Missing or empty translations fall back
to English. Unknown keys, duplicate keys and malformed catalogs are rejected.
Do not translate user content, logs, protocol values or opaque identifiers.

Run `bun run locales:prepare` in ClientApp after modifying HTTP resources. Its
`api.json` frontend catalogs are generated copies, committed for inspection and
offline tooling; change the API-owned resources instead. Build, test, typecheck
and architecture-audit commands run preparation automatically. HTTP catalogs are
embedded in the API assembly; runtime processes never contact Crowdin.

Keep feedback as a message descriptor (`message(key, arguments)`), and render it
with `$message`. `ApiError.displayMessage` and `fieldMessages` retain descriptors;
`ApiError.message` remains readable text in the current language. Use the shared
`utils/message-toast` wrapper for notifications that must follow language changes.

## Crowdin setup

1. Create a Crowdin project with English as its source language and `zh-CN` as a
   target. Use the root `crowdin.yml`; it manages frontend feature JSON and the
   API-owned HTTP catalog, excluding generated frontend HTTP copies.
2. Set the GitHub repository variable `CROWDIN_PROJECT_ID` to the numeric project
   ID and the repository secret `CROWDIN_PERSONAL_TOKEN` to a project-scoped token.
   Grant project/status read, source-file read/write and translation export/build
   permissions needed by the official Action. Never commit credentials.
3. With Crowdin CLI installed and those environment variables set, initialize
   sources and seed the existing Chinese translations once:

   ```sh
   crowdin upload sources --config crowdin.yml
   crowdin upload translations --config crowdin.yml --language zh-CN
   ```

   Source-key migration is complete before this seed. Review imported translations
   in Crowdin; ordinary CI never uploads or overwrites translations.

The `CI` workflow runs on `main` pushes and manual dispatch. It uploads English
sources, downloads all translated Chinese strings (including unapproved ones),
validates the complete candidate, then includes it in the image through the local
Docker build context. No translation PR or periodic synchronization is configured.
The backend-only workflow applies the same process only to HTTP resources and
preserves the deployed frontend from the chosen base image.

Without credentials, CI uses repository catalogs. Upload/download failures or
invalid remote catalogs fall back to the entire repository resource set, with a
warning and CI summary; invalid repository catalogs stop the build. Downloaded
files live in ignored staging paths, never overwrite English sources, and are
applied only after all keys and parameters pass validation. Successful downloads
use English for untranslated entries, rather than stale repository Chinese text.

## Verification and new languages

From the repository root, run `bun test backend/scripts/Translations.test.ts`.
In ClientApp, run `bun run typecheck`, `bun run audit:architecture`, `bun run test`
and `bun run generate`. Backend localization tests use TUnit without external
services; no database migration is involved.

To activate another language, add it to the shared supported-locale lists, request
culture negotiation and frontend loaders; update language selection and date/
number formatting, Crowdin path mapping, download-language configuration and
sync validation/application allowlists. Add its resource files and tests before
making it selectable. Crowdin exports alone do not enable a new UI language.

References: [Crowdin Action](https://github.com/crowdin/github-action),
[Crowdin configuration](https://crowdin.github.io/crowdin-cli/configuration),
[ASP.NET Core request cultures](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/localization/select-language-culture).
