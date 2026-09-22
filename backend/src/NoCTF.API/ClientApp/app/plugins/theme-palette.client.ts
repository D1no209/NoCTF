import { watch } from 'vue'
import { useThemePalette } from '../composables/useThemePalette'
import { colorInk } from '../components/ui/color-picker/color'
import { paletteStorageKey, parseThemePalette } from '../features/theme/palette'

export default defineNuxtPlugin(nuxtApp => {
  const palette = useThemePalette()
  try { palette.value = parseThemePalette(localStorage.getItem(paletteStorageKey)) } catch { /* Browser storage can be unavailable. */ }
  let timer: ReturnType<typeof setTimeout> | undefined
  const save = () => {
    if (timer !== undefined) clearTimeout(timer)
    timer = undefined
    try { localStorage.setItem(paletteStorageKey, JSON.stringify(palette.value)) } catch { /* The current session still keeps its colors. */ }
  }
  const unwatch = watch(palette, value => {
    for (const mode of ['light', 'dark'] as const) {
      const color = value[mode]
      const style = document.documentElement.style
      const wallpaperOpacity = value[mode === 'light' ? 'lightWallpaperOpacity' : 'darkWallpaperOpacity']
      if (color) {
        style.setProperty(`--user-primary-${mode}`, color)
        style.setProperty(`--user-primary-foreground-${mode}`, colorInk(color))
      } else {
        style.removeProperty(`--user-primary-${mode}`)
        style.removeProperty(`--user-primary-foreground-${mode}`)
      }
      style.setProperty(`--user-page-wallpaper-overlay-${mode}`, `${100 - wallpaperOpacity}%`)
    }
    if (timer !== undefined) clearTimeout(timer)
    timer = setTimeout(save, 200)
  }, { deep: true, immediate: true, flush: 'post' })
  window.addEventListener('pagehide', save)
  const synchronize = (event: StorageEvent) => {
    if (event.key === paletteStorageKey || event.key === null) {
      const next = parseThemePalette(event.newValue)
      if (JSON.stringify(next) !== JSON.stringify(palette.value)) palette.value = next
    }
  }
  window.addEventListener('storage', synchronize)
  nuxtApp.vueApp.onUnmount(() => { unwatch(); save(); window.removeEventListener('pagehide', save); window.removeEventListener('storage', synchronize) })
})
