import { defaultWallpaperBlur, type ThemePalette } from '../features/theme/palette'
export function useThemePalette() {
  return useState<ThemePalette>('theme-palette', () => ({
    light: null,
    dark: null,
    lightWallpaperBlur: defaultWallpaperBlur,
    darkWallpaperBlur: defaultWallpaperBlur,
  }))
}
