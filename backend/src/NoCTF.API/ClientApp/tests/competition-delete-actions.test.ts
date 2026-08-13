import { describe, expect, test } from 'bun:test'

describe('competition deletion actions', () => {
  test('preserves the selected delete operation until the async request starts', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('@click="submitDelete"')
    expect(page).toContain("const action = deleteConfirm.value")
    expect(page).not.toMatch(/<AlertDialogAction[\s\S]*?(?:softDelete|hardDelete|submitDelete)/)
    expect(page).toContain(':disabled="deleting || hardDeleting"')
  })

  test('separates soft deletion from server-authorized permanent deletion', async () => {
    const detailPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    const listPage = await Bun.file(
      new URL('../app/pages/admin/competitions/index.vue', import.meta.url),
    ).text()

    expect(detailPage).toContain('const isDeleted = computed(() => !!competition.value?.deletedAt)')
    expect(detailPage).toContain('v-if="!isDeleted"')
    expect(detailPage).toContain('adminPreviewCompetitionHardDelete')
    expect(detailPage).toContain('hardDeletePreview?.canHardDelete')
    expect(detailPage).toContain('v-if="isAdministrator"')
    expect(detailPage).toContain('hardDeletePreview.canForceDelete')
    expect(detailPage).toContain('强制级联删除当前受阻：请先结束比赛并清理全部活动运行环境资源。')
    expect(detailPage).toContain('adminForceDeleteCompetition')
    expect(detailPage).toContain('forceDeleteTitle.value === (competition.value?.title ?? \'\')')
    expect(detailPage).toContain('forceDeleteReason.value.trim().length >= 8')
    expect(detailPage).toContain('平台审计记录已保留')
    expect(detailPage).toContain("reference.code === 'HistoricalEvent'")
    expect(detailPage).toContain('无需先软删除')
    expect(listPage).toContain('query: { includeDeleted: includeDeleted.value }')
    expect(listPage).toContain('<Badge v-if="c.deletedAt" variant="destructive">{{ $t(\'已删除\') }}</Badge>')
  })
})
