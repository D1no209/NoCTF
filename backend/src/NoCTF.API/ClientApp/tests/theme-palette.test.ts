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
    lightWallpaperBlur: 0,
    darkWallpaperBlur: 0,
  })
  expect(parseThemePalette('{"light":"#abc","dark":"url(test)"}')).toEqual({
    light: '#AABBCC',
    dark: null,
    lightWallpaperBlur: 0,
    darkWallpaperBlur: 0,
  })
  expect(parseThemePalette('{"lightWallpaperBlur":-5,"darkWallpaperBlur":140}')).toMatchObject({
    lightWallpaperBlur: 0,
    darkWallpaperBlur: 24,
  })
  expect(parseThemePalette('{"lightWallpaperOpacity":49}').lightWallpaperBlur).toBe(0)
})

test('palette controls wallpaper-only Gaussian blur per theme without changing image strength', async () => {
  const view = await Bun.file(new URL('../app/components/views/theme/ThemePalettePanelView.vue', import.meta.url)).text()
  const feature = await Bun.file(new URL('../app/features/theme/useThemePalettePanel.ts', import.meta.url)).text()
  const plugin = await Bun.file(new URL('../app/plugins/theme-palette.client.ts', import.meta.url)).text()
  const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()
  const slider = await Bun.file(new URL('../app/components/ui/blur-slider/BlurSlider.vue', import.meta.url)).text()

  expect(view).toContain('<BlurSlider')
  expect(view).toContain("$t('palette.wallpaperBlur')")
  expect(feature).toContain('lightWallpaperBlur')
  expect(feature).toContain('darkWallpaperBlur')
  expect(plugin).toContain('--user-page-wallpaper-blur-${mode}')
  expect(css).toContain('--page-wallpaper-blur: var(--user-page-wallpaper-blur-light, 0px);')
  expect(css).toContain('--page-wallpaper-blur: var(--user-page-wallpaper-blur-dark, 0px);')
  expect(css).toContain('filter: blur(var(--page-wallpaper-blur));')
  expect(css).toContain('background: color-mix(in oklch, var(--background) 65%, transparent);')
  expect(view).toContain(':maximum="maximumWallpaperBlur"')
  expect(slider).toContain(':max="maximum"')
  expect(slider).toContain("{{ Math.round(modelValue) }} {{ $t('palette.pixels') }}")
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
