import { expect, test } from 'bun:test'

test('locale changes animate intrinsic control widths within half a second', async () => {
  const locale = await Bun.file(new URL('../app/composables/useLocale.ts', import.meta.url)).text()
  const layout = await Bun.file(new URL('../app/motion/locale-layout.ts', import.meta.url)).text()
  const motion = await Bun.file(new URL('../app/motion/motion.css', import.meta.url)).text()

  expect(locale).toContain('await animateLocaleLayout')
  expect(layout).toContain('localeLayoutMotionDuration = 500')
  expect(layout).toContain("[data-slot='button']")
  expect(layout).toContain("[data-slot='badge']")
  expect(layout).toContain("[data-slot='tabs-trigger']")
  expect(layout).toContain("matchMedia('(prefers-reduced-motion: reduce)')")
  expect(layout).toContain('inlineSize: `${previousWidth}px`')
  expect(layout).toContain('inlineSize: `${nextWidth}px`')
  expect(motion).toContain("[data-locale-resizing='true']")
})
