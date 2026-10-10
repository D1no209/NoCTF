# Contributing to NoCTF

Contributions can improve platform behavior, tests, deployment tooling, translations,
or the user manual. Read the root [AGENTS.md](AGENTS.md) and any instructions in the
directory you change. The current product contract is indexed in [specs](specs/README.md).

## Choose a change

Search existing issues and pull requests before starting. For a substantial product
or architecture change, describe the problem, intended behavior and affected modes
in an issue first. Small fixes and documentation corrections can go directly to a PR.
Use the private process in [SECURITY.md](SECURITY.md) for vulnerabilities.

Keep a PR focused. Describe what users experience before and after the change,
and include validation results. Use synthetic data when reproducing a problem.

## Set up development

Use the .NET SDK selected by the repository's `global.json` files, Bun 1.3.14,
and Docker when running Testcontainers. The [local development guide](docs/development/local.md)
covers dependencies, frontend development and the unified Host.

From the repository root:

```sh
dotnet restore backend/NoCTF.slnx
dotnet build backend/NoCTF.slnx -p:BuildSpaOnPublish=false
```

For the frontend:

```sh
cd backend/src/NoCTF.API/ClientApp
bun install --frozen-lockfile
bun run dev
```

## Follow the project contracts

- Keep HTTP endpoints strongly typed, implement `ExecuteAsync`, and return typed
  HTTP results. Put business rules in Application use cases.
- Use enums or value objects for bounded business concepts and LINQ method syntax.
- Keep Domain, Application and Infrastructure organized by capability.
- Generate EF migrations and snapshots with `dotnet ef`; keep provider-specific
  persistence in its provider project.
- Follow [ClientApp/AGENTS.md](backend/src/NoCTF.API/ClientApp/AGENTS.md) for frontend
  boundaries, shared UI and bilingual catalogs. See [LOCALIZATION.md](LOCALIZATION.md)
  for translation changes.
- Regenerate OpenAPI and the frontend SDK after API contract changes. Regenerate
  Wolverine adapters when changing handler signatures or their DI graph. Do not
  hand-edit generated contracts or adapters.

The root instructions and [development specification](specs/development.md) contain
the full rules. Update the user manual when a change affects a user or operator workflow.

## Validate your change

Run backend checks from `backend`:

```sh
dotnet test tests/NoCTF.Tests/NoCTF.Tests.csproj --treenode-filter '/*/*/*/*[Category!=Integration]' --minimum-expected-tests 1 -p:BuildSpaOnPublish=false
```

For affected integration behavior, use real dependencies. With Docker available:

```sh
dotnet test tests/NoCTF.Tests/NoCTF.Tests.csproj --treenode-filter '/*/*/*/*[Category=Integration]' --maximum-parallel-tests 1 --minimum-expected-tests 38 -p:BuildSpaOnPublish=false
```

Set `NOCTF_REQUIRE_DOCKER_INTEGRATION=true` for a Docker integration gate so that an
unavailable Docker daemon fails the run. Kubernetes and Libvirt checks need the
environments described in [specs/testing.md](specs/testing.md). Report skipped
provider checks separately from passes. SQLite model tests do not prove production
PostgreSQL behavior.

Cross-repository GitOps tests use `NOCTF_GITOPS_TEMPLATE_ROOT` and a compatible
challenge-template checkout. They are separate from the core Docker integration
gate. Check that the selected external revision emits the current named-service
Container contract; legacy Compose challenge definitions are unsupported.

Run frontend checks from `backend/src/NoCTF.API/ClientApp`:

```sh
bun run typecheck
bun run audit:architecture
bun run test
bun run generate
```

For changes in `docs`, run `bun install --frozen-lockfile` and `bun run docs:build`
there. See [documentation contribution instructions](docs/contributing.md).

The fuller backend verification script is `backend/scripts/Verify-Backend.ps1`.
It also checks migration and generated-contract drift. Run it from a clean branch
after committing intended generated changes; its drift checks inspect the working tree.

## Submit a pull request

Use a title such as `fix(runtime): ...`, `feat(competitions): ...`,
`docs: ...`, or `test(scoring): ...`. Explain the concrete problem, resulting
behavior, tests run, and any migration or deployment steps. Add screenshots for
visible UI changes using mock data.

Commit updated Bun lockfiles with dependency changes. Review the staged files
before submitting, and keep build outputs and machine-specific files out of the PR.
Maintainers may request smaller changes or additional tests before merging.

## Licensing and attribution

NoCTF's original work uses [AGPL-3.0-only](LICENSE). Submit contributions only when
you have the right to distribute them under that license. Your contribution remains
attributed to you; submitting a PR does not assign your copyright to a maintainer.

Preserve third-party license and attribution notices. For new images, fonts or other
assets, provide their source and redistribution terms, and update
[ASSET_CREDITS.md](ASSET_CREDITS.md) when appropriate.

Full image builds include LICENSE and produce a `noctf-source-<revision>` artifact from
the actual build workspace, including downloaded translations. When distributing
an image or a modified network service, provide the corresponding source required
by AGPL section 13. Publish the matching source archive with the release or offer
another suitable source download; CI artifact retention is temporary.
For a backend-only image that preserves an existing frontend, retain and provide
the source for both the backend revision and the frontend in the base image. Its
`noctf-source-backend-<revision>` artifact identifies that base image in the source
manifest; it does not replace the base image's matching frontend source archive.
