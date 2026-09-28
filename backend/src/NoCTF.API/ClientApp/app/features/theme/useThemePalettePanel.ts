import { computed, nextTick, ref, watch } from 'vue'
import { Palette } from '@lucide/vue'
import { useThemePalette } from '../../composables/useThemePalette'
import { normalizeHex } from '../../components/ui/color-picker/color'
import { themeColor } from '../../utils/theme-color'
import { defaultWallpaperBlur, maximumWallpaperBlur } from './palette'

export function useThemePalettePanel() {
  const { isDark, toggle } = useTheme()
  const palette = useThemePalette()
  const mode = computed(() => isDark.value ? 'dark' : 'light')
  const defaultColor = ref('#39FF14')
  watch([isDark, palette], async () => { await nextTick(); defaultColor.value = themeColor('--primary') }, { immediate: true, deep: true })
  const color = computed(() => palette.value[mode.value] ?? defaultColor.value)
  const wallpaperBlurKey = computed(() => mode.value === 'dark' ? 'darkWallpaperBlur' : 'lightWallpaperBlur')
  const wallpaperBlur = computed(() => palette.value[wallpaperBlurKey.value])
  function setColor(value: string) {
    const normalized = normalizeHex(value)
    if (normalized) palette.value = { ...palette.value, [mode.value]: normalized }
  }
  function setWallpaperBlur(value: number) {
    if (!Number.isFinite(value)) return
    palette.value = {
      ...palette.value,
      [wallpaperBlurKey.value]: Math.round(Math.min(maximumWallpaperBlur, Math.max(0, value))),
    }
  }
  function setMode(value: 'light' | 'dark') { if (value !== mode.value) toggle() }
  function reset() {
    palette.value = {
      ...palette.value,
      [mode.value]: null,
      [wallpaperBlurKey.value]: defaultWallpaperBlur,
    }
  }
  const presets = [
    { color: '#0066FF', label: 'palette.blue' }, { color: '#00B8D9', label: 'palette.cyan' },
    { color: '#8B5CF6', label: 'palette.violet' }, { color: '#EC4899', label: 'palette.pink' },
    { color: '#F97316', label: 'palette.orange' }, { color: '#39FF14', label: 'palette.green' },
    { color: '#EF4444', label: 'palette.red' }, { color: '#64748B', label: 'palette.slate' },
  ]
  return { Palette, mode, color, wallpaperBlur, maximumWallpaperBlur, presets, setColor, setWallpaperBlur, setMode, reset }
}
export type ThemePalettePanelViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useThemePalettePanel>>
