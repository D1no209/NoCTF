import { describe, expect, test } from 'bun:test'

const readPage = (name: 'runtimes' | 'teams') => Bun.file(
  new URL(`../app/pages/admin/competitions/[id]/${name}.vue`, import.meta.url),
).text()

describe('administrator destructive action wiring', () => {
  test('keeps the runtime target alive until terminate requests are submitted', async () => {
    const page = await readPage('runtimes')

    expect(page).toMatch(/<Button\r?\n\s+type="button"\r?\n\s+variant="destructive"/)
    expect(page).not.toMatch(/<AlertDialogAction[\s\S]*?@click="submit(?:Force)?Termination"/)
  })

  test('uses correction as the only reversal action and validates its reason', async () => {
    const page = await readPage('teams')

    expect(page).not.toContain('adminUnbanTeam')
    expect(page).not.toContain('openUnban')
    expect(page).not.toContain("$t('解封')")
    expect(page).toContain("@click.stop=\"openBan(t, 'correct')\"")
    expect(page).toContain("mode === 'correct' ? length >= 8")
    expect(page).toContain('至少 8 个字符')
  })

  test('keeps manual ban announcements opt-in and sends the generated request field', async () => {
    const page = await readPage('teams')

    expect(page).toContain('const banAnnouncePublicly = ref(false)')
    expect(page).toContain('banAnnouncePublicly.value = false')
    expect(page).toContain('announcePublicly: banAnnouncePublicly.value')
    expect(page).toContain('id="ban-announce-publicly"')
    expect(page).toContain('封禁后发布赛事纪律公告')
  })

  test('lets judges ban teams and resolve appeals without exposing manager-only reversal and registration actions', async () => {
    const page = await readPage('teams')

    expect(page).toContain('const { competitionId, canJudge, canWrite } = useCompetitionAdmin()')
    expect(page).toContain('v-if="canWrite || canJudge"')
    expect(page).toContain('v-if="canWrite && t.registrationStatus === \'Pending\'"')
    expect(page).toContain('v-if="canJudge && !t.isBanned"')
    expect(page).toContain('v-else-if="canWrite"')
    expect(page).toContain('v-if="canJudge && a.appeal?.status === \'Submitted\' && a.canResolve"')
  })
})
