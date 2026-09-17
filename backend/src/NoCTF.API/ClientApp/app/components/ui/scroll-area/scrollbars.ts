import type { Directive } from 'vue'
import { OverlayScrollbars, type OverlayScrollbars as ScrollbarsInstance } from 'overlayscrollbars'

export type ScrollAxis = 'x' | 'y' | 'both'

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
export function createScrollbars(target: HTMLElement, axis: ScrollAxis = 'both'): ScrollbarsBinding {
  const position = getComputedStyle(target).position
  const originalPosition = target.style.getPropertyValue('position')
  const originalPriority = target.style.getPropertyPriority('position')
  const positioned = position === 'fixed' || position === 'absolute'
  const instance = OverlayScrollbars({ target, elements: { viewport: target } }, {
    ...baseOptions,
    overflow: overflowOptions(axis),
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
      instance.options({ scrollbars: { autoHide: 'leave' } })
  })
  target.addEventListener('focusin', focus)
  target.addEventListener('focusout', blur)

  return {
    instance,
    update(nextAxis) {
      instance.options({ overflow: overflowOptions(nextAxis) })
    },
    dispose() {
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

function directiveAxis(target: HTMLElement, value?: ScrollAxis): ScrollAxis {
  const datasetAxis = target.dataset.scrollAxis
  return value ?? (datasetAxis === 'x' || datasetAxis === 'y' || datasetAxis === 'both' ? datasetAxis : 'both')
}

/** Lifecycle fallback for third-party roots that cannot render ScrollSurface. */
export const scrollSurfaceDirective: Directive<HTMLElement, ScrollAxis | undefined> = {
  mounted(target, binding) {
    directiveBindings.set(target, createScrollbars(target, directiveAxis(target, binding.value)))
  },
  updated(target, binding) {
    directiveBindings.get(target)?.update(directiveAxis(target, binding.value))
  },
  unmounted(target) {
    directiveBindings.get(target)?.dispose()
    directiveBindings.delete(target)
  },
}
