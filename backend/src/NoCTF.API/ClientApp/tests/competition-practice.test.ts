import { describe, expect, test } from 'bun:test'
import { canEnterCompetition, isCtfPracticeOpen } from '../app/lib/competition-participation'

describe('competition practice entry', () => {
  const eligible = { registrationStatus: 'Approved', isBanned: false } as const
  const practice = { mode: 'Ctf', status: 'Finished', practiceModeEnabled: true } as const

  test('finished CTF with practice enabled has an entry for approved teams', () => {
    expect(isCtfPracticeOpen(practice)).toBeTrue()
    expect(canEnterCompetition(practice, eligible)).toBeTrue()
    expect(canEnterCompetition({ ...practice, status: 'Running' }, eligible)).toBeTrue()
    expect(isCtfPracticeOpen({ ...practice, status: 'Running' })).toBeFalse()
  })

  test('does not treat a practice setting as permission to enter other lifecycle states or modes', () => {
    for (const status of ['Draft', 'Visible', 'Published', 'Paused'] as const) {
      expect(isCtfPracticeOpen({ ...practice, status })).toBeFalse()
      expect(canEnterCompetition({ ...practice, status }, eligible)).toBeFalse()
    }
    for (const mode of ['Awd', 'Awdp', 'Koh'] as const) {
      expect(canEnterCompetition({ ...practice, mode }, eligible)).toBeFalse()
      expect(canEnterCompetition({ ...practice, mode, status: 'Running' }, eligible)).toBeTrue()
    }
    expect(canEnterCompetition({ ...practice, practiceModeEnabled: false }, eligible)).toBeFalse()
    expect(canEnterCompetition({ mode: 'Ctf', status: 'Finished' }, eligible)).toBeFalse()
    expect(canEnterCompetition(null, eligible)).toBeFalse()
  })

  test('does not bypass registration, team membership or bans', () => {
    expect(canEnterCompetition(practice, null)).toBeFalse()
    expect(canEnterCompetition(practice, { ...eligible, isBanned: true })).toBeFalse()
    for (const registrationStatus of ['Pending', 'Rejected'] as const)
      expect(canEnterCompetition(practice, { ...eligible, registrationStatus })).toBeFalse()
  })

  test('overview and CTF controls share the same practice predicate and use the unscored endpoint', async () => {
    const overview = await Bun.file(new URL('../app/pages/competitions/[id]/index.vue', import.meta.url)).text()
    const panel = await Bun.file(new URL('../app/components/challenges/panels/CtfPanel.vue', import.meta.url)).text()
    const submit = await Bun.file(new URL('../app/components/challenges/FlagSubmit.vue', import.meta.url)).text()
    expect(overview).toContain('canEnterCompetition(competition.value, myTeam.value)')
    expect(overview).toContain("practiceOpen ? $t('进入练习') : $t('进入比赛')")
    expect(panel).toContain('isCtfPracticeOpen(props.competition)')
    expect(panel).toContain(':practice="practiceOpen"')
    expect(submit).toContain('if (props.practice)')
    expect(submit).toContain('await judgePracticeFlag(')
  })
})
