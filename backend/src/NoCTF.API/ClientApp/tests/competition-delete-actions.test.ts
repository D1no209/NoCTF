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
    const [listPage, sidebar] = await Promise.all([
      sourceFile(new URL('../app/pages/competitions/index.vue', import.meta.url)).text(),
      sourceFile(new URL('../app/features/competitions/CompetitionSidebar.vue', import.meta.url)).text(),
    ])

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
    expect(detailPage).toContain('data-slot="hard-delete-impact"')
    expect(detailPage).not.toContain('<Alert v-else-if="canManagePermissions && hardDeletePreview')
    expect(detailPage).toContain("await navigateTo('/competitions')")
    expect(listPage).toContain('adminListCompetitions({ query: { includeDeleted: true } })')
    expect(sidebar).toContain("<Badge v-if=\"item.competition.deletedAt\" variant=\"destructive\">{{ $t('ui.deleted') }}</Badge>")
    expect(sidebar).toContain("value=\"deleted\"")
  })
})
