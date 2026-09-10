import { expect, test } from 'bun:test'

const theme = await Bun.file(new URL('../app/composables/useTheme.ts', import.meta.url)).text()
const toggle = await Bun.file(new URL('../app/components/views/ThemeToggleView.vue', import.meta.url)).text()
const motion = await Bun.file(new URL('../app/motion/motion.css', import.meta.url)).text()

test('theme changes reveal from top to bottom through a feathered boundary', () => {
  expect(theme).toContain('startViewTransition')
  expect(theme).toContain("dataset.themeTransition = 'down'")
  expect(theme).toContain("matchMedia('(prefers-reduced-motion: reduce)')")
  expect(theme).toContain('await nextTick()')
  expect(toggle).toContain(':disabled="themeTransitioning"')
  expect(motion).toContain("html[data-theme-transition='down']::view-transition-new(root)")
  expect(motion).toContain('animation: noctf-theme-cover-down 520ms')
  expect(motion).toContain('#000 calc(100% - 5rem)')
  expect(motion).toContain('rgb(0 0 0 / 0.72) calc(100% - 3rem)')
  expect(motion).toContain('mask-size: 100% 0%')
  expect(motion).toContain('mask-size: 100% calc(100% + 5rem)')
  expect(motion).toContain('@media (prefers-reduced-motion: reduce)')
})
