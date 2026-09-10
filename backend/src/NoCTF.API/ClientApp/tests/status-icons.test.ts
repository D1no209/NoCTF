import { expect, test } from 'bun:test'
import { statusIcons } from '../app/components/ui/icons/status-icons'
import { sourceFile } from './support/feature-source'

test('challenge progress marks use standalone SVG assets', async () => {
  expect(Object.keys(statusIcons)).toEqual(['solved', 'attack-success', 'defense-success', 'attack-defense-success'])
  for (const name of Object.keys(statusIcons)) {
    const svg = await Bun.file(new URL(`../app/assets/svg/status/${name}.svg`, import.meta.url)).text()
    expect(svg).toStartWith('<svg ')
  }
  const component = await sourceFile(new URL('../app/components/ui/icons/StatusIcon.vue', import.meta.url)).text()
  expect(component).not.toContain('<svg')
  expect(component).not.toContain('<path')
  expect(component).toContain('role="img"')
  expect(component).toContain(':aria-label="label"')
  const solved = await Bun.file(new URL('../app/assets/svg/status/solved.svg', import.meta.url)).text()
  expect(solved.match(/<path\b/g)).toHaveLength(2)
  expect(solved).not.toContain('fill="#000"')
})

test('blood ranks use three distinct standalone SVG marks', async () => {
  const marks = await Promise.all(['first', 'second', 'third'].map(rank =>
    Bun.file(new URL(`../app/assets/svg/status/${rank}-blood.svg`, import.meta.url)).text()))
  const component = await Bun.file(new URL('../app/components/ui/icons/BloodMark.vue', import.meta.url)).text()

  expect(new Set(marks).size).toBe(3)
  expect(marks.every(mark => mark.includes('<svg'))).toBe(true)
  expect(component).toContain("rank: 'First' | 'Second' | 'Third'")
  expect(component).toContain('tabindex="0"')
})
