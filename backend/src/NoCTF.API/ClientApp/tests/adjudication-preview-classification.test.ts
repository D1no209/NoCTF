import { expect, test } from 'bun:test'
import { adjudicationCounts, adjudicationSeverity, adjudicationVariant } from '../app/features/admin/adjudication-preview'
import type { NoCTFAPIEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse as Item } from '../app/api/models'

test('deterministic legal history is information rather than an anomaly', () => {
  const information: Item = { differences: [{ kind: 'HistoricalResultChanged', certainty: 'Deterministic', severity: 'Information', classification: 'LegalHistoryChange' }] }
  expect(adjudicationSeverity(information)).toBe('Information')
  expect(adjudicationCounts([information])).toEqual({ Error: 0, Warning: 0, Information: 1 })
  expect(adjudicationVariant('Information')).toBe('secondary')
})

test('each finding row is counted once at its highest severity', () => {
  const mixed: Item = { differences: [{ severity: 'Information' }, { severity: 'Error' }, { severity: 'Warning' }] }
  const review: Item = { differences: [{ severity: 'Warning' }] }
  expect(adjudicationCounts([mixed, review])).toEqual({ Error: 1, Warning: 1, Information: 0 })
  expect(adjudicationSeverity({ differences: [{}] })).toBe('Warning')
})

test('historical analysis is requested explicitly and event pages use the shared cursor lifecycle', async () => {
  const source = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdSubmissionsPage.ts', import.meta.url)).text()
  const mount = source.match(/onMounted\(\(\) => \{([\s\S]*?)\}\)/)?.[1]
  expect(mount).toBeDefined()
  expect(mount).not.toContain('loadPreview')
  expect(source).toContain('includeInformational: previewIncludeInformational.value')
  expect(source).toContain('useCursorPagination<NoCTFAPIEndpointsAdministrationGameplayFactsAdjudicationEventResponse>')
  expect(source).toContain('evidenceAbort?.abort()')
})
