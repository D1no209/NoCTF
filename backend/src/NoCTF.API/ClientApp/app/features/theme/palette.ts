import { normalizeHex } from '../../components/ui/color-picker/color'
export type ThemePalette = { light: string | null; dark: string | null }
export const paletteStorageKey = 'noctf-theme-palette'
export function parseThemePalette(raw: string | null): ThemePalette {
  try {
    const value = JSON.parse(raw ?? '{}')
    return { light: normalizeHex(value?.light), dark: normalizeHex(value?.dark) }
  } catch { return { light: null, dark: null } }
}
