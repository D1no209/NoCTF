import { describe, expect, test } from 'bun:test'
import { readFeatureSource as readFileSync } from './support/feature-source'

const source = readFileSync(
  new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
  'utf8',
)

describe('historical adjudication difference preview', () => {
  test('uses the generated read-only SDK with bounded cursor pagination', () => {
    expect(source).toContain('adminPreviewHistoricalAdjudicationDifferences')
    expect(source).toContain('const cursor = previewCursor.value')
    expect(source).toContain('cursor,')
    expect(source).toContain('limit: 30')
    expect(source).toContain("previewCursor.value = data.nextCursor ?? null")
  })

  test('distinguishes deterministic differences from manual review without an apply action', () => {
    expect(source).toContain("difference.certainty === 'Deterministic'")
    expect(source).toContain("$t('ui.deterministicDifference')")
    expect(source).toContain("$t('ui.needsManualReview')")
    const previewTemplate = source.slice(
      source.indexOf("$t('ui.historicalAdjudicationDifferencePreview')"),
      source.indexOf('<Card>', source.indexOf("$t('ui.historicalAdjudicationDifferencePreview')") + 1),
    )
    expect(previewTemplate).not.toContain('rejudge')
    expect(previewTemplate).not.toContain('纠正按钮')
  })

  test('describes the narrow legacy AWDP break analysis accurately', () => {
    expect(source).toContain("ui.thisPreviewAnalyzesCtfFlagFactsAndAwdpBreakFacts")
    expect(source).toContain('CurrentDuplicateShouldBeCorrect: "ui.theCurrentDuplicateResultComesFromALegacyDefectAnd"')
    expect(source).toContain('MissingAdjudicationRecord: "ui.theCurrentResultHasNoImmutableAdjudicationEvent"')
    expect(source).toContain('TeamEligibilityHistoryRequiresReview: "ui.currentTeamEligibilityCannotProveBloodAwardEligibilityAtThe"')
  })
})
