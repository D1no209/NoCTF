import { describe, expect, test } from 'bun:test'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const generatedSdk = await Bun.file(
  new URL('../src/api/generated/sdk.gen.ts', import.meta.url),
).text()
const generatedTypes = await Bun.file(
  new URL('../src/api/generated/types.gen.ts', import.meta.url),
).text()
const apiSource = await Bun.file(
  new URL('../src/api/cheatIncidentApi.ts', import.meta.url),
).text()
const panelSource = await Bun.file(
  new URL('../src/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue', import.meta.url),
).text()
const resolutionSource = await Bun.file(
  new URL('../src/composables/useCheatIncidentResolution.ts', import.meta.url),
).text()
const workspaceSource = await Bun.file(
  new URL('../src/components/admin/competition-detail/AdminCompetitionDetailWorkspace.vue', import.meta.url),
).text()

describe('cross-team Flag adjudication', () => {
  test('uses generated strongly typed operations without manual routes', () => {
    for (const operation of [
      'adminListCheatIncidents',
      'adminGetCheatIncident',
      'adminDismissCheatIncident',
      'adminConfirmCheatIncident',
      'adminCorrectCheatIncident',
    ]) {
      expect(generatedSdk).toContain(`export const ${operation}`)
      expect(apiSource).toContain(`${operation}(`)
    }

    expect(generatedTypes).toContain(
      'url: \'/api/v1/admin/competitions/{competitionId}/cheat-incidents\'',
    )
    expect(apiSource).not.toMatch(/\/api\/v1|https?:\/\//)
    expect(panelSource).not.toMatch(/\/api\/v1|https?:\/\//)
  })

  test('keeps protected evidence opt-in, audited, and out of browser storage', () => {
    expect(panelSource).toContain('openEvidence(incident)')
    expect(panelSource).toContain('cheatEvidenceAuditHint')
    expect(panelSource).toContain('submittedFlag')
    expect(panelSource).not.toContain('localStorage')
    expect(panelSource).not.toContain('sessionStorage')
  })

  test('supports realtime refetch, polling fallback, and permission-shaped actions', () => {
    expect(panelSource).toContain(`current.on('competitionEventChanged'`)
    expect(panelSource).toContain('refetchInterval: 15_000')
    expect(panelSource).toContain('detail.canDismiss')
    expect(panelSource).toContain('detail.canConfirm')
    expect(panelSource).toContain('detail.canCorrect')
    expect(resolutionSource).toContain('const minimumReasonLength = 8')
    expect(panelSource).toContain('resolutionRemainingCharacters > 0')
    expect(panelSource).toContain('invalidateCheatIncidentResolutionQueries')
    expect(panelSource).toContain('readCheatIncidentResolutionError')
    expect(panelSource).toContain('detail.sourceTeamIsBanned')
  })

  test('integrates a pending-count tab with complete bilingual states', () => {
    expect(workspaceSource).toContain('AdminCompetitionCheatIncidentsPanel')
    expect(workspaceSource).toContain(`{ key: 'cheats'`)
    expect(workspaceSource).toContain('cheatSummary.pendingCount')
    expect(Object.keys(en.admin.competitionDetail.cheatStatuses)).toHaveLength(5)
    expect(Object.keys(zhCN.admin.competitionDetail.cheatStatuses)).toHaveLength(5)
    expect(Object.keys(en.admin.competitionDetail.cheatActions)).toEqual([
      'dismiss',
      'confirm',
      'correct',
    ])
    expect(Object.keys(zhCN.admin.competitionDetail.cheatActions)).toEqual([
      'dismiss',
      'confirm',
      'correct',
    ])
  })
})
