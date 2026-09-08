import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('competition deletion actions', () => {
  test('preserves the selected delete operation until the async request starts', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('@click="submitDelete"')
    expect(page).toContain("const action = deleteConfirm.value")
    expect(page).not.toMatch(/<AlertDialogAction[\s\S]*?(?:softDelete|hardDelete|submitDelete)/)
    expect(page).toContain(':disabled="deleting || hardDeleting"')
  })

  test('separates soft deletion from server-authorized permanent deletion', async () => {
    const detailPage = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    const listPage = await sourceFile(
      new URL('../app/pages/admin/competitions/index.vue', import.meta.url),
    ).text()

    expect(detailPage).toContain('const isDeleted = computed(() => !!competition.value?.deletedAt)')
    expect(detailPage).toContain('v-if="!isDeleted"')
    expect(detailPage).toContain('adminPreviewCompetitionHardDelete')
    expect(detailPage).toContain('hardDeletePreview?.canHardDelete')
    expect(detailPage).toContain('v-if="isAdministrator"')
    expect(detailPage).toContain('hardDeletePreview.canForceDelete')
    expect(detailPage).toContain("ui.forceCascadeDeletionIsCurrentlyBlockedFinishTheCompetitionAnd")
    expect(detailPage).toContain('adminForceDeleteCompetition')
    expect(detailPage).toContain('forceDeleteTitle.value === (competition.value?.title ?? \'\')')
    expect(detailPage).toContain('forceDeleteReason.value.trim().length >= 8')
    expect(detailPage).toContain("ui.theCompetitionAndItsScopedDataWerePermanentlyDeletedThe")
    expect(detailPage).toContain("reference.code === 'HistoricalEvent'")
    expect(detailPage).toContain("ui.impactCheckPassedThisCompetitionHasNoPermanentHistoryOr")
    expect(listPage).toContain('query: { includeDeleted: includeDeleted.value }')
    expect(listPage).toContain("<Badge v-if=\"c.deletedAt\" variant=\"destructive\">{{ $t('ui.deleted') }}</Badge>")
  })
})
