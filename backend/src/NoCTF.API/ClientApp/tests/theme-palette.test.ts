import { expect, test } from 'bun:test'
import { colorInk, hexToHsv, hsvToHex, normalizeHex } from '../app/components/ui/color-picker/color'
import { parseThemePalette } from '../app/features/theme/palette'

test('palette accepts only complete HEX colors and isolates corrupt saved values', () => {
  expect(normalizeHex(' #3f1 ')).toBe('#33FF11')
  expect(normalizeHex('#39ff14')).toBe('#39FF14')
  expect(normalizeHex('red; color: transparent')).toBeNull()
  expect(parseThemePalette('{bad')).toEqual({ light: null, dark: null })
  expect(parseThemePalette('{"light":"#abc","dark":"url(test)"}')).toEqual({ light: '#AABBCC', dark: null })
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
