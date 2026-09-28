import { normalizeHex } from '../../components/ui/color-picker/color'
export const defaultWallpaperBlur = 0
export const maximumWallpaperBlur = 24

export type ThemePalette = {
  light: string | null
  dark: string | null
  lightWallpaperBlur: number
  darkWallpaperBlur: number
}
export const paletteStorageKey = 'noctf-theme-palette'

function normalizeBlur(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.round(Math.min(maximumWallpaperBlur, Math.max(0, value)))
    : defaultWallpaperBlur
}

export function parseThemePalette(raw: string | null): ThemePalette {
  try {
    const value = JSON.parse(raw ?? '{}')
    return {
      light: normalizeHex(value?.light),
      dark: normalizeHex(value?.dark),
      lightWallpaperBlur: normalizeBlur(value?.lightWallpaperBlur),
      darkWallpaperBlur: normalizeBlur(value?.darkWallpaperBlur),
    }
  }
  catch {
    return {
      light: null,
      dark: null,
      lightWallpaperBlur: defaultWallpaperBlur,
      darkWallpaperBlur: defaultWallpaperBlur,
    }
  }
}
