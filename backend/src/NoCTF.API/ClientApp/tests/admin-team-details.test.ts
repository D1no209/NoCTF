import { describe, expect, test } from 'bun:test'

const page = () => Bun.file(new URL('../app/pages/admin/competitions/[id]/teams.vue', import.meta.url)).text()

describe('admin competition team details', () => {
  test('opens a detail sheet from the team name and resolves member profiles through the generated SDK', async () => {
    const source = await page()

    expect(source).toContain('userProfileGet')
    expect(source).toContain('@click="openTeamDetail(t)"')
    expect(source).toContain("$t('队伍详情')")
    expect(source).toContain('member.userId === selectedTeam.captainId')
  })
})
