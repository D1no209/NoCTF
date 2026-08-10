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

  test('shows mutually exclusive delete and restore controls from server state', async () => {
    const detailPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/index.vue', import.meta.url),
    ).text()
    const listPage = await Bun.file(
      new URL('../app/pages/admin/competitions/index.vue', import.meta.url),
    ).text()

    expect(detailPage).toContain('const isDeleted = computed(() => !!competition.value?.deletedAt)')
    expect(detailPage).toContain('v-if="!isDeleted"')
    expect(detailPage).toContain('v-if="isDeleted && canManagePermissions"')
    expect(listPage).toContain('query: { includeDeleted: includeDeleted.value }')
    expect(listPage).toContain('<Badge v-if="c.deletedAt" variant="destructive">{{ $t(\'已删除\') }}</Badge>')
  })
})
