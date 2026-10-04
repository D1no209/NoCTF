import { describe, expect, test } from 'bun:test'
import { readFeatureSource as readFileSync } from './support/feature-source'

const source = readFileSync(
  new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
  'utf8',
)

describe('historical adjudication difference preview', () => {
  test('uses the generated read-only SDK with bounded cursor pagination', () => {
    expect(source).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.gameplayFacts\.adjudicationDifferences\.get\(/)
    expect(source).toContain('const cursor = previewCursor.value')
    expect(source).toContain('cursor: cursor ?? undefined,')
    expect(source).toContain('limit: 30')
    expect(source).toContain("previewCursor.value = data.nextCursor ?? null")
  })

  test('presents severity and evidence separately without an apply action', () => {
    expect(source).toContain('adjudicationSeverityLabel(adjudicationSeverity(item))')
    expect(source).toContain('adjudicationClassificationLabel(difference.classification)')
    expect(source).toContain('adjudicationCompletenessLabel(item.evidenceCompleteness)')
    const previewTemplate = source.slice(
      source.indexOf("$t('administration.label.historicalAdjudicationDifferencePreview')"),
      source.indexOf('<Card>', source.indexOf("$t('administration.label.historicalAdjudicationDifferencePreview')") + 1),
    )
    expect(previewTemplate).not.toContain('rejudge')
    expect(previewTemplate).not.toContain('纠正按钮')
  })

  test('describes current adjudication evidence accurately', () => {
    expect(source).toContain("administration.competitionsBy.description.previewAnalyzesCtfFlag")
    expect(source).not.toContain('CurrentDuplicateShouldBeCorrect')
    expect(source).toContain('MissingAdjudicationRecord: "common.competitionsBy.description.resultImmutableAdjudicationEvent"')
    expect(source).toContain('TeamEligibilityHistoryRequiresReview: "common.competitionsBy.validation.teamEligibilityFormat"')
  })
})
