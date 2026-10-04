import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('participant challenge Flag submit is mounted in the fixed card dock', async () => {
  const [page, detail, ctf, awd, awdp, flag, runtime, css] = await Promise.all([
    sourceFile(new URL('../app/pages/competitions/[id]/challenges/[[ccId]].vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/panels/AwdPanel.vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/panels/AwdpPanel.vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url)).text(),
    sourceFile(new URL('../app/features/challenges/RuntimeCard.vue', import.meta.url)).text(),
    Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text(),
  ])
  expect(page).toContain('id="challenge-flag-dock"')
  expect(page).toContain('flag-dock-target="#challenge-flag-dock"')
  expect(page.indexOf('id="challenge-flag-dock"')).toBeLessThan(page.indexOf('<MotionSwap'))
  expect(detail).toContain(':flag-dock-target="flagDockTarget ?? undefined"')
  expect(detail).toContain('<RemainingAttempts')
  expect(detail).toContain(':count="challenge.remainingFlagAttempts"')
  expect(detail).toContain('font-sans text-2xl font-bold italic')
  expect(detail).toContain('size="icon-sm" class="relative z-10"')
  expect(detail).toContain('<History />')
  expect(detail).not.toContain('class="mt-4 self-start"')
  expect(detail).toContain('@remaining-changed="updateRemainingAttempts"')
  expect(detail).toContain('id="challenge-runtime-dock"')
  expect(detail).toContain('runtime-dock-target="#challenge-runtime-dock"')
  expect(detail).toContain('<FileDown data-icon="inline-start" /> <span class="truncate">{{ attachment.fileName }}</span>')
  expect(detail).not.toContain('<Download data-icon="inline-start" />')
  for (const panel of [ctf, awd, awdp]) expect(panel).toContain(':dock-target="flagDockTarget ?? undefined"')
  for (const panel of [ctf, awd, awdp]) expect(panel).toContain(':dock-target="runtimeDockTarget ?? undefined"')
  expect(flag).toContain("<Teleport :to=\"dockTarget || 'body'\" :disabled=\"!dockTarget\">")
  expect(flag).toContain('data-slot="flag-submit"')
  expect(flag).toContain('<h3 id="flag-submit-title" class="sr-only">')
  expect(flag).not.toContain('<Badge v-if="remainingAttempts')
  expect(runtime).toContain("<Teleport defer :to=\"dockTarget || 'body'\" :disabled=\"!dockTarget\">")
  expect(css).toContain('order: 1; flex: 1 1 0')
  expect(css).toContain('order: 2; flex: none')
  expect(css).toContain("[data-slot='challenge-flag-dock']:empty { display: none; }")
})
