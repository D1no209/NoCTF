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

  test('presents severity and evidence separately without an apply action', () => {
    expect(source).toContain('adjudicationSeverityLabel(adjudicationSeverity(item))')
    expect(source).toContain('adjudicationClassificationLabel(difference.classification)')
    expect(source).toContain('adjudicationCompletenessLabel(item.evidenceCompleteness)')
    const previewTemplate = source.slice(
      source.indexOf("$t('ui.historicalAdjudicationDifferencePreview')"),
      source.indexOf('<Card>', source.indexOf("$t('ui.historicalAdjudicationDifferencePreview')") + 1),
    )
    expect(previewTemplate).not.toContain('rejudge')
    expect(previewTemplate).not.toContain('纠正按钮')
  })

  test('describes current adjudication evidence accurately', () => {
    expect(source).toContain("ui.thisPreviewAnalyzesCtfFlagFactsAndAwdpBreakFacts")
    expect(source).not.toContain('CurrentDuplicateShouldBeCorrect')
    expect(source).toContain('MissingAdjudicationRecord: "ui.theCurrentResultHasNoImmutableAdjudicationEvent"')
    expect(source).toContain('TeamEligibilityHistoryRequiresReview: "ui.currentTeamEligibilityCannotProveBloodAwardEligibilityAtThe"')
  })
})
