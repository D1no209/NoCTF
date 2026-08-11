import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'

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
    expect(source).toContain("$t('确定性差异')")
    expect(source).toContain("$t('需人工复核')")
    const previewTemplate = source.slice(
      source.indexOf("$t('历史裁决差异预览')"),
      source.indexOf('<Card>', source.indexOf("$t('历史裁决差异预览')") + 1),
    )
    expect(previewTemplate).not.toContain('rejudge')
    expect(previewTemplate).not.toContain('纠正按钮')
  })

  test('describes the narrow legacy AWDP break analysis accurately', () => {
    expect(source).toContain('当前分析 CTF Flag，并识别旧版本错误判为重复成就的 AWDP Break；AWD、KoH 与其他事实类型不在此预览中。')
  })
})
