import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

/** 把 CSS 拆成「选择器 → 声明块」,按契约断言而不是逐条字符串匹配。 */
function cssRules(css: string): { selector: string, body: string }[] {
  const rules: { selector: string, body: string }[] = []
  const pattern = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = pattern.exec(css))) rules.push({ selector: match[1]!.trim().replace(/\s+/g, ' '), body: match[2]! })
  return rules
}

const motion = await sourceFile('app/motion/motion.css').text()
const rules = cssRules(motion)

test('background and components animate on their own live layers without snapshots', () => {
  // 背景切换动效:幽灵层位于组件之下,只用 opacity 走合成器(遮罩/剪裁会让整屏逐帧重绘)。
  const ghost = rules.find(rule => rule.selector.includes('[data-wallpaper-ghost]'))
  expect(ghost).toBeDefined()
  expect(ghost!.body).toContain('z-index: -1')
  expect(ghost!.body).toContain('noctf-background-scan-down')
  expect(ghost!.body).toContain('mask-position: bottom')

  // 组件动效:只收敛到承载主题色的表面,禁止对整棵树逐元素过渡。
  const componentMotion = rules.find(rule => rule.body.includes('background-color') && rule.selector.includes("data-slot='card'"))
  expect(componentMotion).toBeDefined()
  expect(componentMotion!.selector).toContain("html[data-theme-transition]")
  expect(motion).not.toContain(':where(*, *::before, *::after)')

  // 快照会把组件压成图层并失去 backdrop-filter 采样,因此不得再使用。
  expect(motion).not.toContain('::view-transition')
})

test('the driver freezes the old background into a ghost layer and skips motion on request', async () => {
  const reveal = await sourceFile('app/motion/reveal-transition.ts').text()

  expect(reveal).toContain('wallpaper.after(ghost)')
  for (const name of ['--background', '--page-wallpaper-image', '--page-wallpaper-blur'])
    expect(reveal).toContain(name)
  expect(reveal).toContain("matchMedia('(prefers-reduced-motion: reduce)')")
  expect(reveal).not.toContain('startViewTransition')
})

test('both switches drive the shared layered driver', async () => {
  const theme = await sourceFile('app/composables/useTheme.ts').text()
  const account = await sourceFile('app/features/account/useAccountPanel.ts').text()

  expect(theme).toContain("runSwitchTransition('theme'")
  expect(account).toContain("runSwitchTransition('wallpaper'")
})
