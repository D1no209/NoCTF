import { describe, expect, test } from 'bun:test'

describe('competition challenge custom title', () => {
  test('allows an optional competition-only title while adding from the question bank', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('v-model="newCustomTitle"')
    expect(page).toContain('customTitle: newCustomTitle.value.trim() || null')
    expect(page).toContain("$t('留空时使用题库模板标题')")
    expect(page).toContain("$t('只修改本场比赛中的展示名称,不会更改题库模板')")
  })

  test('allows restoring the template fallback from challenge settings', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    expect(page).toContain("editCustomTitle.value = c.customTitle ?? ''")
    expect(page).toContain('customTitle: editCustomTitle.value.trim() || null')
    expect(page).toContain('maxlength="160"')
  })
})
