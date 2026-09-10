import { computed, ref } from 'vue'

export type ContentSwapPreset = 'film-up'

const presets = {
  'film-up': { name: 'noctf-film-up', mode: 'out-in' as const },
}

/** Hold the outgoing content's space while Vue sequences exit and entrance. */
export function useContentSwap(preset: ContentSwapPreset = 'film-up') {
  const minimumHeight = ref<string>()
  const release = () => { minimumHeight.value = undefined }
  return {
    frameClass: 'noctf-motion-viewport',
    frameStyle: computed(() => ({ minHeight: minimumHeight.value })),
    transition: {
      ...presets[preset],
      onBeforeLeave: (element: Element) => { minimumHeight.value = `${Math.ceil(element.getBoundingClientRect().height)}px` },
      onAfterEnter: release,
      onEnterCancelled: release,
      onLeaveCancelled: release,
    },
  }
}
