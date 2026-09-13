import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('corner notices remeasure changed content and keep their stack offsets coherent', async () => {
  const frame = await sourceFile('app/components/ui/sonner/NoticeFrame.vue').text()
  const css = await sourceFile('app/assets/css/main.css').text()

  expect(frame).toContain('new ResizeObserver(scheduleMeasurement)')
  expect(frame).toContain("closest<HTMLElement>('[data-sonner-toast]')")
  expect(frame).toContain("item.style.setProperty('--notice-height'")
  expect(frame).toContain("item.style.setProperty('--initial-height'")
  expect(frame).toContain("item.style.setProperty('--offset'")
  expect(frame).toContain("toaster.style.setProperty('--front-toast-height'")
  expect(css).toContain("height: var(--notice-height, auto) !important")
  expect(css).toContain('max-height: min(24rem, calc((100dvh - 8rem) / 2))')
  expect(css).not.toContain('max-height: min(200px')
})
