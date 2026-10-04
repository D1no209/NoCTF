import { onBeforeUnmount, watch, type Ref } from 'vue'

export function waveDisplacement(distance: number, selected: boolean, reduced = false) {
  const influence = reduced ? 0 : Math.exp(-0.5 * (distance / 100) ** 2)
  return { offset: Math.max(selected ? 14 : 0, influence * 30), scale: 1 + influence * 0.018 }
}

/** Pointer-driven motion writes only presentation variables, never selection or Vue state. */
export function useWaveMotion(root: Ref<HTMLElement | null>, selected: () => string | null) {
  let active: HTMLElement | null = null
  let pointerY: number | null = null
  let focusValue: string | null = null
  let frame: number | undefined
  let dirty = true
  let items: Array<{ element: HTMLElement; center: number; value: string; offset?: string | null; scale?: string | null }> = []
  let resize: ResizeObserver | undefined
  let mutation: MutationObserver | undefined
  let media: MediaQueryList | undefined
  const invalidate = () => { dirty = true; schedule() }
  function schedule() {
    if (frame !== undefined || !active) return
    frame = requestAnimationFrame(() => {
      frame = undefined
      if (!active) return
      const top = active.getBoundingClientRect().top
      const scroll = active.querySelector<HTMLElement>('[data-scroll-surface]')?.scrollTop ?? 0
      if (dirty) {
        items = Array.from(active.querySelectorAll<HTMLElement>('[data-wave-item]')).map(element => {
          const rect = element.getBoundingClientRect()
          return { element, center: rect.top - top + scroll + rect.height / 2, value: element.dataset.waveItem ?? '' }
        })
        dirty = false
      }
      const focus = items.find(item => item.value === focusValue)?.center
      const center = pointerY === null ? focus : pointerY - top + scroll
      for (const item of items) {
        const distance = center === undefined ? Infinity : Math.abs(center - item.center)
        const { offset, scale } = waveDisplacement(distance, item.value === selected(), media?.matches)
        const nextOffset = `${offset.toFixed(2)}px`
        const nextScale = scale.toFixed(4)
        if (item.offset !== nextOffset) { item.element.style.setProperty('--wave-offset', nextOffset); item.offset = nextOffset }
        if (item.scale !== nextScale) { item.element.style.setProperty('--wave-scale', nextScale); item.scale = nextScale }
      }
    })
  }
  function clear() {
    resize?.disconnect(); mutation?.disconnect(); media?.removeEventListener('change', invalidate)
    if (frame !== undefined) cancelAnimationFrame(frame)
    frame = undefined; items = []; active = null; pointerY = null; focusValue = null
  }
  watch([root, selected], () => {
    if (root.value !== active) {
      clear(); active = root.value
      if (!active) return
      media = window.matchMedia('(prefers-reduced-motion: reduce)')
      media.addEventListener('change', invalidate)
      resize = new ResizeObserver(invalidate); resize.observe(active)
      mutation = new MutationObserver(invalidate); mutation.observe(active, { childList: true, characterData: true, subtree: true })
      dirty = true
    }
    schedule()
  }, { flush: 'post' })
  onBeforeUnmount(clear)
  return {
    onPointerMove(event: PointerEvent) { if (event.pointerType === 'touch' || media?.matches) return; pointerY = event.clientY; schedule() },
    onPointerLeave() { pointerY = null; schedule() },
    onFocusIn(event: FocusEvent) {
      const item = (event.target as HTMLElement).closest<HTMLElement>('[data-wave-item]')
      focusValue = item?.matches(':focus-visible') ? item.dataset.waveItem ?? null : null
      schedule()
    },
    onFocusOut(event: FocusEvent) { if (!active?.contains(event.relatedTarget as Node | null)) { focusValue = null; schedule() } },
    onScroll: schedule,
  }
}
