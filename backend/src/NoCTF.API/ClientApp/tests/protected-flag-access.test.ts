import { describe, expect, test } from 'bun:test'
import { readFeatureSource as readFileSync } from './support/feature-source'

const source = readFileSync(
  new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
  'utf8',
)

describe('protected Flag access', () => {
  test('loads the Flag immediately without collecting a reason', () => {
    expect(source).toContain('void accessFlag(requestSequence)')
    expect(source).toContain('adminAccessCompetitionGameplayFactValue({')
    expect(source).toContain('v-if="canJudge && (s.kind === \'FlagAttempt\' || s.kind === \'BreakAttempt\')"')
    expect(source).toContain('@click="openFlagAccess(s.id)"')
    expect(source).not.toContain('flagReason')
    expect(source).not.toContain('flag-reason')
  })

  test('explains the role-specific audit behavior and keeps retry feedback', () => {
    expect(source).toContain("$t('ui.platformAdministratorFlagAccessIsNotWrittenToTheAudit')")
    expect(source).toContain("$t('ui.competitionStaffFlagAccessIsWrittenToTheAuditLog')")
    expect(source).toContain('v-else-if="flagError"')
    expect(source).toContain('@click="accessFlag"')
  })

  test('ignores a response after the dialog has switched or closed', () => {
    expect(source).toContain('const requestSequence = ++flagRequestSequence')
    expect(source).toContain('requestSequence !== flagRequestSequence')
    expect(source).toContain('flagDialog.value?.gameplayFactId !== ctx.gameplayFactId')
    expect(source).toContain('function closeFlagAccess()')
  })
})
