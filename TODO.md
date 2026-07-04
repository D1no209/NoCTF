# NoCTF AWDP Handoff TODO

## Current State

- Worktree was clean before this TODO file was added.
- Latest committed checkpoint:
  - `a09309c Implement AWDP round scoring and admin challenge workflow`
- That commit already includes:
  - Independent AWDP plugin flow with Break + Fix + round scoring + attempt limits.
  - AWDP state models, round scoring, patch submission validation jobs, and tests.
  - Challenge bank vs competition scoring separation.
  - Admin challenge creation moved to a full page instead of a dialog card.
  - Challenge type driven admin form for CTF/AWD/AWDP/KoH configuration.
  - CTF asset type selection: static container, dynamic container, static attachment.
  - No attachment behavior when no attachment URL/file is provided.
  - Container challenge image required validation in frontend and backend.
  - Challenge attachment upload endpoint and frontend upload flow.
- Before checkpoint commit, verification passed:
  - `bun run build`
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore`
  - 90 backend tests passed.

## User's Pending Requirements

Continue from commit `a09309c`. Do not redo the already committed AWDP round scoring or admin challenge workflow.

### 1. Replace AWDP checker + EXP flow with one check container

Current implementation in:

- `backend/src/NoCTF.Plugins.AWDP/AwdpPatchService.cs`

Still runs two stages:

- `RunCheckerAsync`
- `RunExpAsync`

Required model:

- Platform runs one `check` container.
- The concrete check script is provided by the challenge author.
- The challenge author configures:
  - check container image
  - check command
- The platform only orchestrates the run and maps return codes.

Use existing fields if possible:

- `CheckerConfig.Image` should become the check container image.
- `CheckerConfig.Command` should become the check command.
- `CheckerConfig.TimeoutSeconds` should be the check timeout.

Avoid adding EXP as a separate AWDP phase.

### 2. Rename AWDP admin labels

Frontend labels need to change for AWDP context:

- "检查容器" should be called `check 容器` in both Chinese and English UI.
- `EXP 命令` should be called `check 命令`.
- The check process covers both exploitability and service availability.

Likely files:

- `frontend/src/components/admin/ChallengeTemplateForm.vue`
- `frontend/src/locales/zh-CN.json`
- `frontend/src/locales/en.json`

Be careful not to break AWD checker terminology if AWD still needs ordinary checker labels.

### 3. Implement AWDP check return code mapping

Required check script return values:

- `0`: Fix success.
- `1`: EXP exploit succeeded.
- `2`: Bad/cheating patch, for example sandboxing or malicious hardening.
- `3`: Interaction error.
- Timeout: service error.

Internal mapping should be:

- `0` -> `AwdpFixStatus.FixSuccess`, `AwdpServiceStatus.ServiceOk`
- `1` -> `AwdpFixStatus.FixFailed`, `AwdpServiceStatus.ServiceOk`
- `2` -> service-error-like failure for players, but retain internal detail as bad patch / rule violation.
- `3` -> `AwdpFixStatus.FixServiceError`, `AwdpServiceStatus.ServiceError`
- timeout -> `AwdpFixStatus.FixServiceError` or `FixTimeout` internally, but player-visible result must be service error.

Do not make `FixFailed` default to penalty. Existing scoring rule says FixFailed normally just receives no defense score.

### 4. Player-visible AWDP defense results

Players should only see three result categories:

- `防御成功`
- `防御异常：EXP利用成功`
- `防御异常：服务异常`

Internal detail may still be stored for admins/operators.

Important files:

- `backend/src/NoCTF.API/Endpoints/Competitions/GetPatchSubmissionsEndpoint.cs`
- `backend/src/NoCTF.Plugins.AWDP/AwdpModeProvider.cs`
- `frontend/src/components/game/ChallengeModal.vue`
- `frontend/src/locales/zh-CN.json`
- `frontend/src/locales/en.json`

Current issue:

- `PatchSubmissionDto.ValidationDetail` and `state.LastValidationDetail` are exposed to the participant UI.
- Need to sanitize or replace participant-visible detail with the three categories above.

### 5. Pass submitted patch archive to the check container

The platform must give the submitted patch archive to the check container so the challenge author's check script can detect bad patches.

Current helper:

- `BuildPatchEnvironment` includes:
  - `PATCH_URL`
  - `PATCH_FILE_NAME`
  - `FIX_ENTRY`

Current probe helper:

- `BuildProbeEnvironment` only includes:
  - `TARGET_HOST`
  - `TARGET_PORT`
  - `TEAM_ID`

Required:

- The check container environment must include both target information and patch archive information.
- Suggested env vars:
  - `TARGET_HOST`
  - `TARGET_PORT`
  - `TEAM_ID`
  - `PATCH_URL`
  - `PATCH_FILE_NAME`
  - `FIX_ENTRY`

### 6. Add AWDP patch package template upload/download

Need a challenge-bank asset for an AWDP patch package template.

Recommended backend model fields:

- Add `PatchTemplateUrl` to `ChallengeTemplate`.
- Add `PatchTemplateUrl` to `Challenge`.
- Copy it when binding a template to a competition.
- Expose it in admin DTOs and participant challenge DTOs.

Likely backend files:

- `backend/src/NoCTF.Core/Entities.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/GetChallengesAdminEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/UpdateChallengeEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs`
- `backend/src/NoCTF.API/Endpoints/Competitions/GetChallengesEndpoint.cs`
- `backend/src/NoCTF.Infrastructure/Migrations/20260703120000_AddAwdpRoundStateModel.cs` or a new migration.
- `backend/src/NoCTF.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`, if maintaining snapshots manually in this repo style.

Recommended upload endpoint:

- `POST /api/admin/challenges/{id}/patch-template`

Can mirror:

- `backend/src/NoCTF.API/Endpoints/Admin/ChallengeAttachmentEndpoint.cs`

Storage path suggestion:

- `challenge-patch-templates/{challengeId}/{timestamp}-{safeName}`

Frontend files:

- `frontend/src/api/noctf.ts`
- `frontend/src/components/admin/ChallengeTemplateForm.vue`
- `frontend/src/views/admin/AdminChallengeCreateView.vue`
- `frontend/src/views/admin/AdminChallengesView.vue`
- `frontend/src/components/game/ChallengeModal.vue`
- locale files

UI behavior:

- Admin challenge form should allow patch template upload for AWDP challenges.
- Admin edit flow should upload a replacement patch template after update.
- Participant AWDP challenge modal should show "download patch template" when `patchTemplateUrl` exists.

### 7. Build an AWDP authoring template

Current template directory:

- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web`

Current problem:

- `README.md` still describes checker + EXP as old two-stage behavior.
- `checker/check.py` currently only checks `/health`.

Required template:

- Platform runs check script.
- Check script returns:
  - `0` fix success
  - `1` EXP exploit succeeded
  - `2` bad/cheating patch
  - `3` interaction error
  - timeout handled by platform as service error
- Template should demonstrate:
  - Vulnerable target container.
  - FixScript patch package example.
  - Check container that receives target and patch env vars.
  - Check script that can download/inspect patch archive before probing the target.

Suggested files to add or update:

- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web/README.md`
- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web/checker/check.py`
- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web/checker/Dockerfile`
- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web/patch-example/fix.sh`
- Add a `patch-template/` folder or similar source directory for the downloadable template package.

### 8. Update tests

Current AWDP tests:

- `backend/tests/NoCTF.Tests/AwdpPatchServiceTests.cs`

Tests currently assume:

- sandbox run
- checker run
- EXP run

Need to update them to the new single check container flow:

- check exit `0` verifies fix.
- check exit `1` records exploit success / fix failed.
- check exit `2` records service-error-visible result with internal bad patch detail.
- check exit `3` records service error.
- check timeout records service error.
- check environment includes `PATCH_URL`, `PATCH_FILE_NAME`, `FIX_ENTRY`, `TARGET_HOST`, `TARGET_PORT`, `TEAM_ID`.

Also consider tests for patch template field mapping:

- Template DTO includes `PatchTemplateUrl`.
- Binding template copies `PatchTemplateUrl` to competition challenge.

### 9. Verification checklist

After implementation, run:

```powershell
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore
```

```powershell
cd frontend
bun run build
```

Also run:

```powershell
git diff --check
```

## Important Constraints

- AWDP must stay an independent plugin, not an AWD sub-mode.
- Do not move AWDP logic into platform core except generic plugin routing, auth, file storage, Docker scheduling, scoring persistence, and event dispatch.
- Do not change CTF, AWD, or dynamic flag behavior while implementing AWDP check changes.
- Do not reintroduce AWDP initial score.
- Do not make FixFailed default to penalty.
- Do not expose detailed bad-patch or sandbox-detection detail to players.
- Keep challenge bank responsible for assets only: containers, attachments, check container config, patch template.
- Keep scoring and per-round policy in competition/deployed challenge config.

## Files Most Likely To Touch

Backend:

- `backend/src/NoCTF.Core/Entities.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/ChallengeAttachmentEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/GetChallengesAdminEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/UpdateChallengeEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs`
- `backend/src/NoCTF.API/Endpoints/Competitions/GetChallengesEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Competitions/GetPatchSubmissionsEndpoint.cs`
- `backend/src/NoCTF.Plugins.AWDP/AwdpPatchService.cs`
- `backend/src/NoCTF.Plugins.AWDP/AwdpModeProvider.cs`
- `backend/src/NoCTF.Plugins.AWDP/Templates/basic-web/**`
- `backend/tests/NoCTF.Tests/AwdpPatchServiceTests.cs`

Frontend:

- `frontend/src/api/noctf.ts`
- `frontend/src/components/admin/ChallengeTemplateForm.vue`
- `frontend/src/views/admin/AdminChallengeCreateView.vue`
- `frontend/src/views/admin/AdminChallengesView.vue`
- `frontend/src/components/game/ChallengeModal.vue`
- `frontend/src/views/CompetitionDetailView.vue`
- `frontend/src/locales/zh-CN.json`
- `frontend/src/locales/en.json`
