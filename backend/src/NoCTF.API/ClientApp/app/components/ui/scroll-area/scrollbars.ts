import type { Directive } from 'vue'
import { OverlayScrollbars, type OverlayScrollbars as ScrollbarsInstance } from 'overlayscrollbars'

export type ScrollAxis = 'x' | 'y' | 'both'

interface ScrollbarsOptions {
  pinHorizontalTo?: HTMLElement | null
}

interface ScrollbarPinRect {
  top: number
  right: number
  bottom: number
  left: number
}

export interface HorizontalScrollbarPinPosition {
  top: number
  left: number
  width: number
}

export function horizontalScrollbarPinPosition(
  target: ScrollbarPinRect,
  scrollRoot: ScrollbarPinRect,
  scrollTop: number,
  scrollLeft: number,
  scrollbarSize: number,
): HorizontalScrollbarPinPosition | null {
  const visibleTop = Math.max(target.top, scrollRoot.top)
  const visibleRight = Math.min(target.right, scrollRoot.right)
  const visibleBottom = Math.min(target.bottom, scrollRoot.bottom)
  const visibleLeft = Math.max(target.left, scrollRoot.left)
  if (visibleBottom <= visibleTop || visibleRight <= visibleLeft) return null

  return {
    top: scrollTop + visibleBottom - scrollRoot.top - scrollbarSize,
    left: scrollLeft + visibleLeft - scrollRoot.left,
    width: visibleRight - visibleLeft,
  }
}

const baseOptions = {
  scrollbars: {
    theme: 'os-theme-noctf',
    autoHide: 'leave' as const,
    autoHideDelay: 800,
    autoHideSuspend: false,
    dragScroll: true,
    clickScroll: 'instant' as const,
  },
}

function overflowOptions(axis: ScrollAxis) {
  return {
    x: axis === 'y' ? 'hidden' as const : 'scroll' as const,
    y: axis === 'x' ? 'hidden' as const : 'scroll' as const,
  }
}

export interface ScrollbarsBinding {
  instance: ScrollbarsInstance
  update: (axis: ScrollAxis) => void
  dispose: () => void
}

/** Enhances one owned viewport and releases every listener with its owner. */
export function createScrollbars(
  target: HTMLElement,
  axis: ScrollAxis = 'both',
  options: ScrollbarsOptions = {},
): ScrollbarsBinding {
  const position = getComputedStyle(target).position
  const originalPosition = target.style.getPropertyValue('position')
  const originalPriority = target.style.getPropertyPriority('position')
  const positioned = position === 'fixed' || position === 'absolute'
  const idleAutoHide = options.pinHorizontalTo ? 'never' as const : 'leave' as const
  const instance = OverlayScrollbars({
    target,
    elements: { viewport: target },
    ...(options.pinHorizontalTo ? { scrollbars: { slot: options.pinHorizontalTo } } : {}),
  }, {
    ...baseOptions,
    overflow: overflowOptions(axis),
    ...(options.pinHorizontalTo ? { scrollbars: { ...baseOptions.scrollbars, autoHide: idleAutoHide } } : {}),
  })

  if (positioned)
    target.style.setProperty('position', position)

  const hasKeyboardFocus = () => target.matches(':focus-visible') || Boolean(target.querySelector(':focus-visible'))
  const focus = () => {
    if (hasKeyboardFocus())
      instance.options({ scrollbars: { autoHide: 'never' } })
  }
  const blur = () => queueMicrotask(() => {
    if (!hasKeyboardFocus())
      instance.options({ scrollbars: { autoHide: idleAutoHide } })
  })
  target.addEventListener('focusin', focus)
  target.addEventListener('focusout', blur)

  const pinnedRoot = options.pinHorizontalTo
  const pinnedScrollbar = pinnedRoot ? instance.elements().scrollbarHorizontal.scrollbar : null
  let pinFrame = 0
  let pinObserver: ResizeObserver | undefined
  let pinIntersectionObserver: IntersectionObserver | undefined
  const updatePinnedScrollbar = () => {
    if (!pinnedRoot || !pinnedScrollbar) return
    const position = horizontalScrollbarPinPosition(
      target.getBoundingClientRect(),
      pinnedRoot.getBoundingClientRect(),
      pinnedRoot.scrollTop,
      pinnedRoot.scrollLeft,
      Math.max(pinnedScrollbar.offsetHeight, 8),
    )
    pinnedScrollbar.hidden = position === null
    if (!position) return
    pinnedScrollbar.style.setProperty('top', `${position.top}px`)
    pinnedScrollbar.style.setProperty('right', 'auto')
    pinnedScrollbar.style.setProperty('bottom', 'auto')
    pinnedScrollbar.style.setProperty('left', `${position.left}px`)
    pinnedScrollbar.style.setProperty('width', `${position.width}px`)
  }
  const schedulePinnedScrollbarUpdate = () => {
    if (pinFrame) return
    pinFrame = requestAnimationFrame(() => {
      pinFrame = 0
      updatePinnedScrollbar()
    })
  }
  if (pinnedRoot && pinnedScrollbar) {
    pinnedScrollbar.classList.add('os-scrollbar-pinned-horizontal')
    pinnedRoot.addEventListener('scroll', schedulePinnedScrollbarUpdate, { passive: true })
    window.addEventListener('resize', schedulePinnedScrollbarUpdate, { passive: true })
    pinObserver = new ResizeObserver(schedulePinnedScrollbarUpdate)
    pinObserver.observe(target)
    pinObserver.observe(pinnedRoot)
    pinIntersectionObserver = new IntersectionObserver(schedulePinnedScrollbarUpdate, { root: pinnedRoot })
    pinIntersectionObserver.observe(target)
    schedulePinnedScrollbarUpdate()
  }

  return {
    instance,
    update(nextAxis) {
      instance.options({ overflow: overflowOptions(nextAxis) })
    },
    dispose() {
      if (pinFrame) cancelAnimationFrame(pinFrame)
      pinObserver?.disconnect()
      pinIntersectionObserver?.disconnect()
      pinnedRoot?.removeEventListener('scroll', schedulePinnedScrollbarUpdate)
      if (pinnedRoot)
        window.removeEventListener('resize', schedulePinnedScrollbarUpdate)
      target.removeEventListener('focusin', focus)
      target.removeEventListener('focusout', blur)
      instance.destroy()
      if (!positioned)
        return
      if (originalPosition)
        target.style.setProperty('position', originalPosition, originalPriority)
      else
        target.style.removeProperty('position')
    },
  }
}

const directiveBindings = new WeakMap<HTMLElement, ScrollbarsBinding>()

function pinnedHorizontalRoot(target: HTMLElement): HTMLElement | null {
  if (target.dataset.pinHorizontalScrollbar !== 'nearest-y') return null
  return target.parentElement?.closest<HTMLElement>("[data-scroll-surface][data-scroll-axis='y']") ?? null
}

function directiveAxis(target: HTMLElement, value?: ScrollAxis): ScrollAxis {
  const datasetAxis = target.dataset.scrollAxis
  return value ?? (datasetAxis === 'x' || datasetAxis === 'y' || datasetAxis === 'both' ? datasetAxis : 'both')
}

/** Lifecycle fallback for third-party roots that cannot render ScrollSurface. */
export const scrollSurfaceDirective: Directive<HTMLElement, ScrollAxis | undefined> = {
  mounted(target, binding) {
    directiveBindings.set(target, createScrollbars(
      target,
      directiveAxis(target, binding.value),
      { pinHorizontalTo: pinnedHorizontalRoot(target) },
    ))
  },
  updated(target, binding) {
    directiveBindings.get(target)?.update(directiveAxis(target, binding.value))
  },
  unmounted(target) {
    directiveBindings.get(target)?.dispose()
    directiveBindings.delete(target)
  },
}
