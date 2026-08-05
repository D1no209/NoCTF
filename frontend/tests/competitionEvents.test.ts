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
  new URL('../src/api/competitionEventApi.ts', import.meta.url),
).text()
const panelSource = await Bun.file(
  new URL('../src/components/competition-events/CompetitionEventsPanel.vue', import.meta.url),
).text()
const workspaceSource = await Bun.file(
  new URL('../src/components/competition-detail/CompetitionDetailWorkspace.vue', import.meta.url),
).text()

describe('competition event log', () => {
  test('uses only generated strongly typed HTTP operations', () => {
    for (const operation of [
      'listCompetitionEvents',
      'adminExportCompetitionEvents',
      'adminAccessCompetitionSubmissionFlag',
    ]) {
      expect(generatedSdk).toContain(`export const ${operation}`)
      expect(apiSource).toContain(`${operation}(`)
    }

    expect(generatedTypes).toContain(
      'url: \'/api/v1/competitions/{competitionId}/events\'',
    )
    expect(generatedTypes).toContain(
      'url: \'/api/v1/admin/competitions/{competitionId}/events/export\'',
    )
    expect(generatedTypes).toContain(
      'url: \'/api/v1/admin/competitions/{competitionId}/submissions/{submissionId}/flag-access\'',
    )
    expect(apiSource).not.toMatch(/\/api\/v1|https?:\/\//)
    expect(panelSource).not.toMatch(/\/api\/v1|https?:\/\//)
  })

  test('keeps realtime payloads as refetch hints and clears revealed flags', () => {
    expect(panelSource).toContain(`current.on('competitionEventChanged'`)
    expect(panelSource).toContain('invalidateQueries')
    expect(panelSource).toContain('revealedFlag.value = null')
    expect(panelSource).not.toContain('localStorage')
    expect(panelSource).not.toContain('sessionStorage')
  })

  test('adds the fourth competition workspace tab with bilingual copy', () => {
    expect(workspaceSource).toContain('<CompetitionEventsPanel')
    expect(workspaceSource).toContain('value="events"')
    expect(en.competitionEvents.title).toBeTruthy()
    expect(zhCN.competitionEvents.title).toBeTruthy()
    expect(Object.keys(en.competitionEvents.kinds)).toHaveLength(43)
    expect(Object.keys(zhCN.competitionEvents.kinds)).toHaveLength(43)
  })
})
