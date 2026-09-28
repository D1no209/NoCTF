import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('corner notices remeasure changed content and keep their stack offsets coherent', async () => {
  const frame = await sourceFile('app/components/ui/sonner/NoticeFrame.vue').text()
  const css = await sourceFile('app/assets/css/main.css').text()
  const layout = await sourceFile('app/features/shell/DefaultLayout.vue').text()
  const notificationNotice = await sourceFile('app/features/notifications/showNotificationNotice.ts').text()

  expect(frame).toContain('new ResizeObserver((entries) =>')
  expect(frame).toContain('width === observedWidth && height === observedHeight')
  expect(frame).toContain('setPropertyIfChanged')
  expect(frame).toContain("closest<HTMLElement>('[data-sonner-toast]')")
  expect(frame).toContain("setPropertyIfChanged(item, '--notice-height'")
  expect(frame).toContain("setPropertyIfChanged(item, '--initial-height'")
  expect(frame).toContain("setPropertyIfChanged(item, '--offset'")
  expect(frame).toContain("setPropertyIfChanged(toaster, '--front-toast-height'")
  expect(css).toContain("height: var(--notice-height, auto) !important")
  expect(css).toContain('max-height: min(24rem, calc((100dvh - 8rem) / 2))')
  expect(css).not.toContain('max-height: min(200px')
  expect(notificationNotice).toContain("position: 'top-right'")
  expect(notificationNotice).toContain('NoticeToastComponent')
  expect(notificationNotice).toContain('NotificationNoticeContentComponent')
  expect(layout).toContain('showNotificationNotice')
  expect(layout).toContain('notificationBaselineReady')
})
