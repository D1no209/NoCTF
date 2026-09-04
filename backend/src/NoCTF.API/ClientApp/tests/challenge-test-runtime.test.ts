import { describe, expect, test } from 'bun:test'

const panel = await Bun.file(
  new URL('../app/components/admin/ChallengeTestRuntimePanel.vue', import.meta.url),
).text()
const page = await Bun.file(
  new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
).text()

describe('challenge template test runtime', () => {
  test('uses generated SDK operations for the full lifecycle', () => {
    expect(panel).toContain('adminChallengeBankGetTestRuntime')
    expect(panel).toContain('adminChallengeBankStartTestRuntime')
    expect(panel).toContain('adminChallengeBankStopTestRuntime')
    expect(panel).toContain('adminChallengeBankResetTestRuntime')
    expect(panel).toContain('adminChallengeBankExtendTestRuntime')
    expect(panel).not.toContain("fetch('/api")
  })

  test('shows access URLs, protected test Flag and every asynchronous state', () => {
    expect(panel).toContain('<RuntimeAccessUrl')
    expect(panel).toContain('runtime.testFlag')
    expect(panel).toContain('navigator.clipboard.writeText')
    expect(panel).toContain("value?.state === 'Running' && value.flagState === 'Pending'")
    expect(panel).toContain('const busy = computed(() => acting.value)')
    expect(panel).toContain("runtime?.state === 'Stopping'")
    expect(panel).toContain('<Skeleton v-if="loading"')
    expect(panel).toContain('<Alert v-else-if="loadError" variant="destructive">')
    expect(panel).toContain('<Alert v-else-if="timedOut" variant="destructive">')
  })

  test('runs only saved definitions and explains image-cache prewarming', () => {
    expect(page).toContain('const runtimeDefinitionDirty = computed(() => {')
    expect(page).toContain('<ChallengeTestRuntimePanel')
    expect(page).toContain(':definition-dirty="runtimeDefinitionDirty"')
    expect(panel).toContain(':disabled="busy || definitionDirty"')
    expect(panel).toContain('停止测试实例只清理容器与网络，不会主动删除 Runner 节点的镜像缓存。')
  })
})
