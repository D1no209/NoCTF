import { describe, expect, test } from 'bun:test'

describe('challenge workspace responsive feedback', () => {
  test('uses the full available row for the wide challenge workspace', async () => {
    const theme = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

    expect(theme).toContain(
      '.competition-participant-workspace.challenge-workspace { grid-template-rows: minmax(0, 1fr); }',
    )
    expect(theme).toContain(
      '.competition-participant-workspace:not(.question-participant-workspace) { grid-template-rows: minmax(0, 2fr) minmax(0, 1fr); }',
    )
    expect(theme).toContain(
      '.question-participant-workspace { grid-template-rows: minmax(0, 3fr) minmax(0, 1fr); }',
    )
    expect(theme).not.toContain('grid-template-rows: minmax(0, 1fr) 12rem;')
  })

  test('keeps automatic challenge state in its component instead of reopening corner notices', async () => {
    const [fix, runtime, awdp, navigator, root] = await Promise.all([
      Bun.file(new URL('../app/components/views/challenges/FixSubmitView.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/views/challenges/RuntimeCardView.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/views/challenges/panels/AwdpPanelView.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/views/competition/CompetitionChallengeNavigatorView.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/views/app/ApplicationRootView.vue', import.meta.url)).text(),
    ])

    expect(fix).toContain('v-else-if="validating || recycling || completedAndRecycled"')
    expect(fix).toContain('role="status" aria-live="polite"')
    expect(fix.match(/<Alert\b/g)).toHaveLength(1)
    expect(runtime).not.toContain("<Alert v-if=\"isRunning && (runtime.access?.route === 'Gateway'")
    expect(awdp).not.toContain('<Alert v-if="state?.fixActivation"')
    expect(navigator).not.toContain('<Alert v-else-if="board.processing.value"')
    expect(root).toContain(':visible-toasts="2"')
    expect(root).not.toMatch(/<Sonner[^>]*\bexpand\b/)
  })
})
