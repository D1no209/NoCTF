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

test('the scan runs on a top layer while components animate in place', () => {
  // 扫描光带在最顶层:固定定位、pointer-events none、只做 transform 合成。
  const veil = rules.find(rule => rule.selector.includes('[data-switch-scan-veil]'))
  expect(veil).toBeDefined()
  expect(veil!.body).toContain('pointer-events: none')
  expect(veil!.body).toContain('noctf-scan-sweep')
  expect(motion).toContain('translate3d(0, calc(100vh + 38vh), 0)')

  // 组件动效:有界容器集合,且不含 box-shadow(逐元素阴影过渡会整屏重绘)。
  const componentMotion = rules.find(rule => rule.selector.includes("data-slot='card-header'") && rule.body.includes('transition-property'))
  expect(componentMotion).toBeDefined()
  expect(componentMotion!.body).not.toContain('box-shadow')

  // 不再使用快照或背景幽灵层。
  expect(motion).not.toContain('::view-transition')
  expect(motion).not.toContain('wallpaper-ghost')
})

test('the driver sweeps a top layer and skips motion on request', async () => {
  const driver = await sourceFile('app/motion/reveal-transition.ts').text()

  expect(driver).toContain('document.body.appendChild(veil)')
  expect(driver).toContain("matchMedia('(prefers-reduced-motion: reduce)')")
  expect(driver).not.toContain('startViewTransition')
})

test('both switches drive the shared driver', async () => {
  const theme = await sourceFile('app/composables/useTheme.ts').text()
  const account = await sourceFile('app/features/account/useAccountPanel.ts').text()

  expect(theme).toContain("runSwitchTransition('theme'")
  expect(account).toContain("runSwitchTransition('wallpaper'")
})
