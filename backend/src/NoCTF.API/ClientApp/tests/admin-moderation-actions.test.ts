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

  test('opens a confirmation before unbanning and validates correction reasons', async () => {
    const page = await readPage('teams')

    expect(page).toContain("@click.stop=\"openUnban(t)\"")
    expect(page).toContain('const unbanDialog = ref<')
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

  test('lets judges ban teams without exposing manager-only reversal and registration actions', async () => {
    const page = await readPage('teams')

    expect(page).toContain('const { competitionId, canJudge, canWrite } = useCompetitionAdmin()')
    expect(page).toContain('v-if="canWrite || canJudge"')
    expect(page).toContain('v-if="canWrite && t.registrationStatus === \'Pending\'"')
    expect(page).toContain('v-if="canJudge && !t.isBanned"')
    expect(page).toContain('v-else-if="canWrite"')
    expect(page).toContain('v-if="canWrite && a.appeal?.status === \'Submitted\' && a.canResolve"')
  })
})
