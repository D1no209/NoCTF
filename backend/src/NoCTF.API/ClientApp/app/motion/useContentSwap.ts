import { computed, ref, toValue, type MaybeRefOrGetter } from 'vue'

export type ContentSwapPreset = 'film-up' | 'film-left' | 'film-right'

const presets = {
  'film-up': { name: 'noctf-film-up', mode: 'out-in' as const },
  'film-left': { name: 'noctf-film-left', mode: 'out-in' as const },
  'film-right': { name: 'noctf-film-right', mode: 'out-in' as const },
}

/** Hold the outgoing content's space while Vue sequences exit and entrance. */
export function useContentSwap(preset: MaybeRefOrGetter<ContentSwapPreset> = 'film-up') {
  const minimumHeight = ref<string>()
  const release = () => { minimumHeight.value = undefined }
  return {
    frameClass: 'noctf-motion-viewport',
    frameStyle: computed(() => ({ minHeight: minimumHeight.value })),
    transition: {
      get name() { return presets[toValue(preset)].name },
      mode: 'out-in' as const,
      onBeforeLeave: (element: Element) => { minimumHeight.value = `${Math.ceil(element.getBoundingClientRect().height)}px` },
      onAfterEnter: release,
      onEnterCancelled: release,
      onLeaveCancelled: release,
    },
  }
}
