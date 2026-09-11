import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { canEnterCompetition, canRegisterForCompetition, isCtfPracticeOpen } from '../app/lib/competition-participation'

describe('competition practice entry', () => {
  const eligible = { registrationStatus: 'Approved', isBanned: false } as const
  const practice = { mode: 'Ctf', status: 'Finished', practiceModeEnabled: true } as const

  test('post-contest practice allows late teams without reopening ordinary registration', () => {
    expect(canRegisterForCompetition(practice)).toBeTrue()
    expect(canRegisterForCompetition({ ...practice, practiceModeEnabled: false })).toBeFalse()
    expect(canRegisterForCompetition({ ...practice, mode: 'Awdp' })).toBeFalse()
    expect(canRegisterForCompetition({ ...practice, status: 'Running' })).toBeFalse()
    expect(canRegisterForCompetition({ ...practice, status: 'Paused' })).toBeFalse()
  })

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

  test('overview and CTF controls share the same practice predicate and use the standard fact endpoint', async () => {
    const overview = await sourceFile(new URL('../app/features/competitions/CompetitionOverview.vue', import.meta.url)).text()
    const panel = await sourceFile(new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url)).text()
    const submit = await sourceFile(new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url)).text()
    const detail = await sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text()
    expect(overview).toContain('canEnterCompetition(competition.value, myTeam.value)')
    expect(overview).toContain("practiceOpen ? $t('ui.enterPractice') : $t('ui.enterTheCompetition')")
    expect(panel).toContain('isCtfPracticeOpen(props.competition)')
    expect(panel).toContain(':practice="practiceOpen"')
    expect(submit).toContain('await submitFlagEndpoint({')
    expect(submit).not.toContain('judgePracticeFlag')
    expect(panel).toContain("result === 'Correct' && !practiceOpen.value")
    expect(detail).toContain('ctx.competition.value?.status')
  })
})
