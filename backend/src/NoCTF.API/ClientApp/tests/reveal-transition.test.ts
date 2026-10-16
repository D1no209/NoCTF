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

test('the scan wipes the old background under live components', () => {
  // 扫描擦除:幽灵层在组件之下(z-index -1),用遮罩自上而下移出旧背景。
  const ghost = rules.find(rule => rule.selector.includes('[data-wallpaper-ghost]'))
  expect(ghost).toBeDefined()
  expect(ghost!.body).toContain('z-index: -1')
  expect(ghost!.body).toContain('noctf-background-scan-down')

  // 组件动效:有界容器集合(逐元素过渡会让切换帧长到 800ms),且不含 box-shadow。
  const componentMotion = rules.find(rule => rule.selector.includes("data-slot='card-header'") && rule.body.includes('transition-property'))
  expect(componentMotion).toBeDefined()
  expect(componentMotion!.body).not.toContain('box-shadow')

  // 不使用 View Transitions 快照。
  expect(motion).not.toContain('::view-transition')
})

test('components change colour when the scan line reaches them', async () => {
  const driver = await sourceFile('app/motion/reveal-transition.ts').text()

  expect(driver).toContain('wallpaper.after(ghost)')
  expect(driver).toContain('scanTimeForOffset')
  expect(driver).toContain("matchMedia('(prefers-reduced-motion: reduce)')")
  expect(driver).not.toContain('startViewTransition')
})

test('both switches drive the shared scan driver', async () => {
  const theme = await sourceFile('app/composables/useTheme.ts').text()
  const account = await sourceFile('app/features/account/useAccountPanel.ts').text()

  expect(theme).toContain("runSwitchTransition('theme'")
  expect(account).toContain("runSwitchTransition('wallpaper'")
})
