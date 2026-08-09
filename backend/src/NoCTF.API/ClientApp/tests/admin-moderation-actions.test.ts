import { describe, expect, test } from 'bun:test'

const readPage = (name: 'runtimes' | 'teams') => Bun.file(
  new URL(`../app/pages/admin/competitions/[id]/${name}.vue`, import.meta.url),
).text()

describe('administrator destructive action wiring', () => {
  test('keeps the runtime target alive until terminate requests are submitted', async () => {
    const page = await readPage('runtimes')

    expect(page).toContain('<Button\n            type="button"\n            variant="destructive"')
    expect(page).not.toMatch(/<AlertDialogAction[\s\S]*?@click="submit(?:Force)?Termination"/)
  })

  test('opens a confirmation before unbanning and validates correction reasons', async () => {
    const page = await readPage('teams')

    expect(page).toContain("@click.stop=\"openUnban(t)\"")
    expect(page).toContain('const unbanDialog = ref<')
    expect(page).toContain("mode === 'correct' ? length >= 8")
    expect(page).toContain('至少 8 个字符')
  })
})
