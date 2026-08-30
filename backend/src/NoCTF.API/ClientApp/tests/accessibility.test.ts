import { describe, expect, test } from 'bun:test'

async function source(path: string) {
  return Bun.file(new URL(path, import.meta.url)).text()
}

describe('keyboard and icon accessibility', () => {
  test('opens clickable table rows with Enter or Space and exposes their purpose', async () => {
    const pages = await Promise.all([
      '../app/pages/admin/platform/users.vue',
    ].map(source))

    for (const page of pages) {
      expect(page).toContain('role="button"')
      expect(page).toContain('tabindex="0"')
      expect(page).toContain('@keydown.enter="openDetail(')
      expect(page).toContain('@keydown.space.prevent="openDetail(')
      expect(page).toContain(':aria-label="$t(')
    }

    const leaderboard = await source('../app/pages/competitions/[id]/leaderboard.vue')
    expect(leaderboard).toContain('<button')
    expect(leaderboard).toContain('type="button"')
    expect(leaderboard).toContain('@click="openDetail(team, column)"')
    expect(leaderboard).toContain('focus-visible:ring-2')
  })

  test('labels dialog and sheet close buttons', async () => {
    const dialog = await source('../app/components/ui/dialog/DialogContent.vue')
    const sheet = await source('../app/components/ui/sheet/SheetContent.vue')

    expect(dialog).toContain(':aria-label="$t(\'关闭\')"')
    expect(sheet).toContain(':aria-label="$t(\'关闭\')"')
  })
})
