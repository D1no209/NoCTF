import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const panel = await sourceFile(
  new URL('../app/features/admin/ChallengeTestRuntimePanel.vue', import.meta.url),
).text()
const page = await sourceFile(
  new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
).text()

describe('challenge template test runtime', () => {
  test('uses generated SDK operations for the full lifecycle', () => {
    expect(panel).toContain('adminChallengeBankGetTestRuntime')
    expect(panel).toContain('adminChallengeBankCreateTestRuntime')
    expect(panel).toContain('adminChallengeBankStopTestRuntime')
    expect(panel).toContain('replacesRuntimeId')
    expect(panel).toContain('adminChallengeBankExtendTestRuntime')
    expect(panel).toContain('evaluateChallengeTestRuntimePolling')
    expect(panel).toContain('const outcome = await load()')
    expect(panel).not.toContain("fetch('/api")
  })

  test('shows access URLs, protected test Flag and every asynchronous state', () => {
    expect(panel).toContain("<component :is=\"RuntimeAccessUrl\"")
    expect(panel).toContain('runtime.testFlag')
    expect(panel).toContain('navigator.clipboard.writeText')
    expect(panel).toContain('shouldContinuePolling(outcome)')
    expect(panel).toContain('const busy = computed(() => acting.value || waitingForAcceptedRuntime.value)')
    expect(panel).toContain("runtime?.state === 'Stopping'")
    expect(panel).toContain('<Skeleton v-if="loading"')
    expect(panel).toContain('<Alert v-else-if="loadError" variant="destructive">')
    expect(panel).toContain('<Alert v-else-if="timedOut" variant="destructive">')
  })

  test('runs only saved definitions and explains image-cache prewarming', () => {
    expect(page).toContain('const runtimeDefinitionDirty = computed(() => {')
    expect(page).toContain("<component :is=\"ChallengeTestRuntimePanel\"")
    expect(page).toContain(':definition-dirty="runtimeDefinitionDirty"')
    expect(panel).toContain(':disabled="busy || definitionDirty"')
    expect(panel).toContain("runtime.challengeTest.description.stoppingTestInstanceRemoves")
  })
})
