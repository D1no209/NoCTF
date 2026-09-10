import { onBeforeUnmount } from 'vue'

/** Pointer feedback stays in the primitive and never changes calendar selection. */
export function useCalendarPointerLight() {
  let frame: number | undefined
  let surface: HTMLElement | undefined
  let x = 0
  let y = 0

  function clearLight() {
    if (frame !== undefined) cancelAnimationFrame(frame)
    frame = undefined
    surface?.removeAttribute('data-pointer-active')
    surface = undefined
  }

  function moveLight(event: PointerEvent) {
    if (event.pointerType !== 'mouse' || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
    surface = event.currentTarget as HTMLElement
    x = event.clientX
    y = event.clientY
    if (frame !== undefined) return
    frame = requestAnimationFrame(() => {
      frame = undefined
      if (!surface) return
      const bounds = surface.getBoundingClientRect()
      surface.style.setProperty('--calendar-pointer-x', `${x - bounds.left}px`)
      surface.style.setProperty('--calendar-pointer-y', `${y - bounds.top}px`)
      surface.setAttribute('data-pointer-active', '')
    })
  }

  onBeforeUnmount(clearLight)
  return { moveLight, clearLight }
}
