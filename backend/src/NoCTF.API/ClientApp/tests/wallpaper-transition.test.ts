import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('wallpaper switching reuses the reveal while isolating it to the background layer', async () => {
  const account = await sourceFile('app/features/account/useAccountPanel.ts').text()
  const layout = await sourceFile('app/components/views/layout/DefaultLayoutView.vue').text()
  const css = await sourceFile('app/assets/css/main.css').text()
  const motion = await sourceFile('app/motion/motion.css').text()

  expect(account).toContain("runDownRevealTransition('wallpaper'")
  expect(layout).toContain('data-slot="page-wallpaper"')
  expect(layout).toContain('data-slot="default-layout-foreground"')
  expect(layout.indexOf('data-slot="page-wallpaper"')).toBeLessThan(layout.indexOf('<header'))
  expect(css).toContain('view-transition-name: noctf-page-wallpaper')
  expect(css).toContain('view-transition-name: noctf-page-foreground')
  expect(motion).toContain("html[data-wallpaper-transition='down']::view-transition-new(noctf-page-wallpaper)")
  expect(motion).not.toContain("html[data-wallpaper-transition='down']::view-transition-new(root)")
  expect(motion).not.toContain("html[data-wallpaper-transition='down']::view-transition-new(noctf-page-foreground)")
  expect(motion).toContain("html[data-wallpaper-transition='down']::view-transition-group(noctf-page-foreground)")
})
