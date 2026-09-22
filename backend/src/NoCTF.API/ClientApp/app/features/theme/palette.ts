import { normalizeHex } from '../../components/ui/color-picker/color'
export const defaultWallpaperOpacity = 35

export type ThemePalette = {
  light: string | null
  dark: string | null
  lightWallpaperOpacity: number
  darkWallpaperOpacity: number
}
export const paletteStorageKey = 'noctf-theme-palette'

function normalizeOpacity(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.round(Math.min(100, Math.max(0, value)))
    : defaultWallpaperOpacity
}

export function parseThemePalette(raw: string | null): ThemePalette {
  try {
    const value = JSON.parse(raw ?? '{}')
    return {
      light: normalizeHex(value?.light),
      dark: normalizeHex(value?.dark),
      lightWallpaperOpacity: normalizeOpacity(value?.lightWallpaperOpacity),
      darkWallpaperOpacity: normalizeOpacity(value?.darkWallpaperOpacity),
    }
  }
  catch {
    return {
      light: null,
      dark: null,
      lightWallpaperOpacity: defaultWallpaperOpacity,
      darkWallpaperOpacity: defaultWallpaperOpacity,
    }
  }
}
