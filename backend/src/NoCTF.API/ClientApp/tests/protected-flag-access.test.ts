import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'

const source = readFileSync(
  new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
  'utf8',
)

describe('protected Flag access', () => {
  test('loads the Flag immediately without collecting a reason', () => {
    expect(source).toContain('void accessFlag(requestSequence)')
    expect(source).toContain('adminAccessCompetitionGameplayFactValue({')
    expect(source).toContain('v-if="canJudge" variant="ghost" size="sm" @click="openFlagAccess(s.id)"')
    expect(source).not.toContain('flagReason')
    expect(source).not.toContain('flag-reason')
  })

  test('explains the role-specific audit behavior and keeps retry feedback', () => {
    expect(source).toContain("$t('平台管理员读取 Flag 不记录审计日志。')")
    expect(source).toContain("$t('比赛工作人员读取 Flag 会记录审计日志。')")
    expect(source).toContain('v-else-if="flagError"')
    expect(source).toContain('@click="() => accessFlag()"')
  })

  test('ignores a response after the dialog has switched or closed', () => {
    expect(source).toContain('const requestSequence = ++flagRequestSequence')
    expect(source).toContain('requestSequence !== flagRequestSequence')
    expect(source).toContain('flagDialog.value?.gameplayFactId !== ctx.gameplayFactId')
    expect(source).toContain('function closeFlagAccess()')
  })
})
