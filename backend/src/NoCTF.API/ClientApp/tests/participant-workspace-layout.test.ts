import { describe, expect, test } from 'bun:test'

const page = (path: string) => Bun.file(new URL(path, import.meta.url)).text()

describe('participant competition workspace layout', () => {
  test('routes the four participant pages through the same shell as challenges', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')

    for (const path of [
      '`${base}/challenges`',
      '`${base}/leaderboard`',
      '`${base}/questions`',
      '`${base}/my/team`',
      '`${base}/my/submissions`',
    ]) {
      expect(parent).toContain(path)
    }
    expect(parent).toContain('competition && usesParticipantWorkspace')
  })

  test('keeps staff-only live screens, teams and events out of the participant navigation', async () => {
    const parent = await page('../app/pages/competitions/[id].vue')
    const adminParent = await page('../app/pages/admin/competitions/[id].vue')

    expect(parent).toContain('isControlScreen')
    expect(parent).not.toContain("label: translate(\"中控大屏\")")
    expect(parent).not.toContain("label: translate(\"3D 大屏\")")
    expect(parent).not.toContain("label: translate(\"队伍\")")
    expect(parent).not.toContain("label: translate(\"动态\")")
    expect(adminParent).toContain("label: translate(\"中控大屏\")")
    expect(adminParent).toContain("label: translate(\"3D 大屏\")")
    expect(adminParent).toContain("label: translate(\"队伍管理\")")
    expect(adminParent).toContain("label: translate(\"动态\")")
  })

  test('keeps challenge navigation, page content and broadcast in one reusable three-column shell', async () => {
    const shell = await page('../app/components/competition/CompetitionParticipantWorkspace.vue')

    expect(shell).toContain('xl:grid-cols-[15rem_minmax(0,1fr)_19rem]')
    expect(shell).toContain('<CompetitionChallengeNavigator')
    expect(shell).toContain('<slot />')
    expect(shell).toContain('<CompetitionWorkspaceNavigation')
    expect(shell).toContain('<CompetitionBroadcastPanel')
  })

  test('uses the shared participant shell on all five workspace pages', async () => {
    const sources = await Promise.all([
      page('../app/pages/competitions/[id]/challenges/index.vue'),
      page('../app/pages/competitions/[id]/leaderboard.vue'),
      page('../app/pages/competitions/[id]/questions.vue'),
      page('../app/pages/competitions/[id]/my/team.vue'),
      page('../app/pages/competitions/[id]/my/submissions.vue'),
    ])

    for (const source of sources)
      expect(source).toContain('<CompetitionParticipantWorkspace')
  })
})
