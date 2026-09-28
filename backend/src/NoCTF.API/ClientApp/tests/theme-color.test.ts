import { describe, expect, test } from 'bun:test'
import { oklchToHex } from '../app/utils/theme-color'
import { sourceFile } from './support/feature-source'

describe('Canvas theme color conversion', () => {
  test('converts design-system OKLCH tokens to distinct sRGB colors', () => {
    expect(oklchToHex('oklch(0.56 0.2 25)')).toBe('#d02b31')
    expect(oklchToHex('oklch(0.52 0.15 150)')).toBe('#007f35')
    expect(oklchToHex('oklch(56% 0.2 25deg)')).toBe('#d02b31')
  })

  test('leaves non-OKLCH colors to the browser converter', () => {
    expect(oklchToHex('#39ff14')).toBeNull()
    expect(oklchToHex('var(--primary)')).toBeNull()
  })

  test('re-resolves Canvas colors after initial CSS and theme state settle', async () => {
    const chart = await sourceFile(
      new URL('../app/components/ui/chart/MiniChart.vue', import.meta.url),
    ).text()

    expect(chart).toContain('const palette = useThemePalette()')
    expect(chart).toContain('function scheduleRender()')
    expect(chart.match(/requestAnimationFrame/g)?.length).toBeGreaterThanOrEqual(3)
    expect(chart).toContain('document.fonts.ready.then(scheduleRender)')
    expect(chart).toContain("attributeFilter: ['class', 'style']")
    expect(chart).toContain('watch([isDark, palette]')
    expect(chart).toContain('scheduleRender()')
    expect(chart).toContain('themeObserver?.disconnect()')
  })
})
