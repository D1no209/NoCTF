import { defaultWallpaperBlur, type ThemePalette } from '../lib/theme-palette'
export function useThemePalette() {
  return useState<ThemePalette>('theme-palette', () => ({
    light: null,
    dark: null,
    lightWallpaperBlur: defaultWallpaperBlur,
    darkWallpaperBlur: defaultWallpaperBlur,
  }))
}
