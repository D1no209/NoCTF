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
  host: ScrollbarPinRect,
  scrollbarSize: number,
  hostScrollTop = 0,
  hostScrollLeft = 0,
): HorizontalScrollbarPinPosition | null {
  const visibleTop = Math.max(target.top, scrollRoot.top)
  const visibleRight = Math.min(target.right, scrollRoot.right)
  const visibleBottom = Math.min(target.bottom, scrollRoot.bottom)
  const visibleLeft = Math.max(target.left, scrollRoot.left)
  if (visibleBottom <= visibleTop || visibleRight <= visibleLeft) return null

  return {
    top: hostScrollTop + visibleBottom - host.top - scrollbarSize,
    left: hostScrollLeft + visibleLeft - host.left,
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
  const pinnedRoot = options.pinHorizontalTo
  // Keep the control outside scrolling content so compositor scrolls cannot move it.
  const pinnedHost = pinnedRoot?.parentElement
  const hostPosition = pinnedHost?.style.getPropertyValue('position') ?? ''
  const hostPriority = pinnedHost?.style.getPropertyPriority('position') ?? ''
  const positionHost = pinnedHost && getComputedStyle(pinnedHost).position === 'static'
  if (positionHost) pinnedHost.style.setProperty('position', 'relative')
  const idleAutoHide = pinnedHost ? 'never' as const : 'leave' as const
  const instance = OverlayScrollbars({
    target,
    elements: { viewport: target },
    ...(pinnedHost ? { scrollbars: { slot: pinnedHost } } : {}),
  }, {
    ...baseOptions,
    overflow: overflowOptions(axis),
    ...(pinnedHost ? { scrollbars: { ...baseOptions.scrollbars, autoHide: idleAutoHide } } : {}),
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

  const pinnedScrollbar = pinnedHost ? instance.elements().scrollbarHorizontal.scrollbar : null
  let previousPinPosition: HorizontalScrollbarPinPosition | null = null
  let pinFrame = 0
  let pinObserver: ResizeObserver | undefined
  let pinIntersectionObserver: IntersectionObserver | undefined
  let pinGeometryDirty = true
  let pinGeometry: {
    target: ScrollbarPinRect
    root: ScrollbarPinRect
    host: ScrollbarPinRect
    scrollTop: number
    scrollLeft: number
    size: number
  } | undefined
  const updatePinnedScrollbar = () => {
    if (!pinnedRoot || !pinnedHost || !pinnedScrollbar) return
    // Ordinary scrolling only changes the table's offset in this owned viewport.
    // Measure layout after a resize/content change, never on each scroll frame.
    if (pinGeometryDirty || !pinGeometry) {
      pinGeometry = {
        target: target.getBoundingClientRect(),
        root: pinnedRoot.getBoundingClientRect(),
        host: pinnedHost.getBoundingClientRect(),
        scrollTop: pinnedRoot.scrollTop,
        scrollLeft: pinnedRoot.scrollLeft,
        size: Math.max(pinnedScrollbar.offsetHeight, 8),
      }
      pinGeometryDirty = false
    }
    const deltaY = pinnedRoot.scrollTop - pinGeometry.scrollTop
    const deltaX = pinnedRoot.scrollLeft - pinGeometry.scrollLeft
    const position = horizontalScrollbarPinPosition(
      {
        top: pinGeometry.target.top - deltaY,
        bottom: pinGeometry.target.bottom - deltaY,
        left: pinGeometry.target.left - deltaX,
        right: pinGeometry.target.right - deltaX,
      },
      pinGeometry.root,
      pinGeometry.host,
      pinGeometry.size,
      pinnedHost.scrollTop,
      pinnedHost.scrollLeft,
    )
    if (pinnedScrollbar.hidden !== (position === null))
      pinnedScrollbar.hidden = position === null
    if (!position) return
    if (position.top !== previousPinPosition?.top)
      pinnedScrollbar.style.setProperty('top', `${position.top}px`)
    if (position.left !== previousPinPosition?.left)
      pinnedScrollbar.style.setProperty('left', `${position.left}px`)
    if (position.width !== previousPinPosition?.width)
      pinnedScrollbar.style.setProperty('width', `${position.width}px`)
    previousPinPosition = position
  }
  const schedulePinnedScrollbarUpdate = () => {
    if (pinFrame) return
    pinFrame = requestAnimationFrame(() => {
      pinFrame = 0
      updatePinnedScrollbar()
    })
  }
  const invalidatePinGeometry = () => {
    pinGeometryDirty = true
    schedulePinnedScrollbarUpdate()
  }
  const updateAfterTransition = (event: TransitionEvent) => {
    if (event.propertyName === 'transform') invalidatePinGeometry()
  }
  if (pinnedRoot && pinnedHost && pinnedScrollbar) {
    pinnedScrollbar.classList.add('os-scrollbar-pinned-horizontal')
    pinnedScrollbar.style.setProperty('right', 'auto')
    pinnedScrollbar.style.setProperty('bottom', 'auto')
    pinnedRoot.addEventListener('scroll', schedulePinnedScrollbarUpdate, { passive: true })
    pinnedRoot.addEventListener('transitionend', updateAfterTransition)
    window.addEventListener('resize', invalidatePinGeometry, { passive: true })
    pinObserver = new ResizeObserver(invalidatePinGeometry)
    pinObserver.observe(target)
    pinObserver.observe(pinnedRoot)
    pinObserver.observe(pinnedHost)
    // A preceding chart/filter can move the table without resizing the table itself.
    for (let ancestor = target.parentElement; ancestor && ancestor !== pinnedRoot; ancestor = ancestor.parentElement)
      pinObserver.observe(ancestor)
    pinIntersectionObserver = new IntersectionObserver(invalidatePinGeometry, { root: pinnedRoot })
    pinIntersectionObserver.observe(target)
    schedulePinnedScrollbarUpdate()
  }

  return {
    instance,
    update(nextAxis) {
      instance.options({ overflow: overflowOptions(nextAxis) })
      if (pinnedScrollbar) invalidatePinGeometry()
    },
    dispose() {
      if (pinFrame) cancelAnimationFrame(pinFrame)
      pinObserver?.disconnect()
      pinIntersectionObserver?.disconnect()
      pinnedRoot?.removeEventListener('scroll', schedulePinnedScrollbarUpdate)
      pinnedRoot?.removeEventListener('transitionend', updateAfterTransition)
      if (pinnedRoot)
        window.removeEventListener('resize', invalidatePinGeometry)
      target.removeEventListener('focusin', focus)
      target.removeEventListener('focusout', blur)
      instance.destroy()
      if (positionHost && pinnedHost) {
        if (hostPosition) pinnedHost.style.setProperty('position', hostPosition, hostPriority)
        else pinnedHost.style.removeProperty('position')
      }
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
