import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('challenge options collapse to one control row and reveal the existing detail on hover', async () => {
  const navigator = await sourceFile(
    new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url),
  ).text()
  const main = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()
  const motion = await Bun.file(new URL('../app/motion/motion.css', import.meta.url)).text()
  const challengeCss = main.slice(
    main.indexOf("[data-challenge-navigator] [data-slot='wave-selection-list']"),
    main.indexOf("[data-slot='type-watermark']"),
  )

  expect(navigator).toContain('data-challenge-item')
  expect(navigator).toContain('class="relative block h-28 min-w-0 flex-1"')
  expect(navigator).not.toContain(':data-solved=')
  expect(navigator).toContain('data-challenge-item-summary')
  expect(navigator).toContain('data-challenge-item-details')
  expect(navigator.indexOf('{{ item.challenge.title }}')).toBeLessThan(navigator.indexOf('data-challenge-item-details'))
  expect(navigator.indexOf('{{ currentScore(item.challenge.id) }}')).toBeLessThan(navigator.indexOf('data-challenge-item-details'))

  expect(main).toContain("[data-challenge-navigator] [data-slot='wave-selection-item'] {")
  expect(main).toContain('height: 40px; min-height: 0; padding: 0; overflow: visible;')
  expect(main).toContain(":has([data-slot='wave-selection-item'][aria-selected='true']) { padding-block-end: 32px; }")
  expect(main).toContain('z-index: 30; height: 112px;')
  expect(main).toContain("[data-slot='wave-selection-item'][aria-selected='true'] {")
  expect(challengeCss).not.toContain("[aria-selected='true'] {\n  overflow: visible;\n  box-shadow:")
  expect(main).toContain('background: transparent;\n  pointer-events: none;')
  expect(main).toContain("[data-slot='wave-selection-item']:is(:hover, :focus-visible, [aria-selected='true']) [data-challenge-item-panel] { pointer-events: auto; }")
  expect(main).toContain("[data-slot='wave-selection-item'][aria-selected='true'] [data-slot='icon-watermark'].noctf-motion-challenge-watermark { opacity: 0.24; }")
  expect(main).not.toContain("[data-challenge-navigator] .noctf-disclosure-content[data-state='open'] { overflow: visible; }")
  expect(challengeCss).not.toContain(":has([data-challenge-item][data-solved='true'])")
  expect(challengeCss).not.toContain('background: color-mix(in oklch, var(--card) 92%, transparent)')
  expect(challengeCss).not.toContain('box-shadow:')
  expect(challengeCss).toContain("[data-challenge-item-panel] {\n  border-radius: 0 22px 22px 0;\n  background: transparent;")
  expect(main).toContain(".challenge-workspace [data-slot='floating-sidebar'] {")
  expect(main).toContain('position: relative; inset: auto; width: 100%; height: 24rem;')

  expect(motion).toContain("[data-challenge-navigator] .noctf-wave-item")
  expect(motion).toContain('height 800ms')
  expect(motion).toContain(".noctf-wave-item:hover:not(:focus-visible, [aria-selected='true'])")
  expect(motion).toContain('transition-delay: 0ms, 50ms, 0ms, 0ms')
  expect(motion).toContain("[data-slot='wave-selection-item']:hover:not(:focus-visible, [aria-selected='true'])")
  expect(motion).toContain('transition-delay: 50ms')
  expect(motion).toContain('opacity 800ms')
  expect(motion).toContain('transform 800ms')
  expect(motion).toContain("[data-slot='wave-selection-item'][aria-selected='true'] .noctf-motion-challenge-details")
  expect(motion).toContain("[data-slot='wave-selection-item']:is(:hover, :focus-visible)")
  expect(motion).toContain('@media (hover: none), (pointer: coarse)')
  expect(motion).toContain('@media (prefers-reduced-motion: reduce)')
  expect(motion).not.toContain('max-height 800ms')
})
