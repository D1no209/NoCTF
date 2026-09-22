import { expect, test } from 'bun:test'
import { colorInk, hexToHsv, hsvToHex, normalizeHex } from '../app/components/ui/color-picker/color'
import { parseThemePalette } from '../app/features/theme/palette'

test('palette accepts only complete HEX colors and isolates corrupt saved values', () => {
  expect(normalizeHex(' #3f1 ')).toBe('#33FF11')
  expect(normalizeHex('#39ff14')).toBe('#39FF14')
  expect(normalizeHex('red; color: transparent')).toBeNull()
  expect(parseThemePalette('{bad')).toEqual({
    light: null,
    dark: null,
    lightWallpaperOpacity: 35,
    darkWallpaperOpacity: 35,
  })
  expect(parseThemePalette('{"light":"#abc","dark":"url(test)"}')).toEqual({
    light: '#AABBCC',
    dark: null,
    lightWallpaperOpacity: 35,
    darkWallpaperOpacity: 35,
  })
  expect(parseThemePalette('{"lightWallpaperOpacity":-5,"darkWallpaperOpacity":140}')).toMatchObject({
    lightWallpaperOpacity: 0,
    darkWallpaperOpacity: 100,
  })
})

test('palette exposes a shared background opacity control and persists it as a theme-specific value', async () => {
  const view = await Bun.file(new URL('../app/components/views/theme/ThemePalettePanelView.vue', import.meta.url)).text()
  const feature = await Bun.file(new URL('../app/features/theme/useThemePalettePanel.ts', import.meta.url)).text()
  const plugin = await Bun.file(new URL('../app/plugins/theme-palette.client.ts', import.meta.url)).text()
  const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(view).toContain('<OpacitySlider')
  expect(view).toContain("$t('palette.wallpaperOpacity')")
  expect(feature).toContain('lightWallpaperOpacity')
  expect(feature).toContain('darkWallpaperOpacity')
  expect(plugin).toContain('--user-page-wallpaper-overlay-${mode}')
  expect(css).toContain('--page-wallpaper-overlay: var(--user-page-wallpaper-overlay-light, 65%);')
  expect(css).toContain('--page-wallpaper-overlay: var(--user-page-wallpaper-overlay-dark, 65%);')
})

test('palette preserves representative colors through HSV conversion including achromatic colors', () => {
  for (const color of ['#000000', '#FFFFFF', '#808080', '#39FF14', '#0066FF', '#EC4899', '#F97316']) {
    const { h, s, v } = hexToHsv(color)
    expect(hsvToHex(h, s, v)).toBe(color)
  }
  expect(hsvToHex(360, 100, 100)).toBe('#FF0000')
  expect(hsvToHex(120, 200, 200)).toBe('#00FF00')
})

test('button ink adapts to bright and dark custom backgrounds', () => {
  expect(colorInk('#FFFFFF')).toBe('#101820')
  expect(colorInk('#39FF14')).toBe('#101820')
  expect(colorInk('#000000')).toBe('#F8FAFC')
  expect(colorInk('#0066FF')).toBe('#F8FAFC')
})
