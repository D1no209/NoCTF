import { OverlayScrollbars, type OverlayScrollbars as ScrollbarsInstance } from 'overlayscrollbars'

const options = {
  scrollbars: {
    theme: 'os-theme-noctf',
    autoHide: 'leave' as const,
    autoHideDelay: 800,
    autoHideSuspend: false,
    dragScroll: true,
    clickScroll: 'instant' as const,
  },
}

/** Enhance declared UI viewports in place, preserving Vue's DOM and native scroll coordinates. */
export function installScrollbars() {
  const active = new Map<HTMLElement, { instance: ScrollbarsInstance; dispose: () => void }>()
  const connect = (target: HTMLElement) => {
    if (active.has(target)) return
    const position = getComputedStyle(target).position
    const originalPosition = target.style.getPropertyValue('position')
    const originalPriority = target.style.getPropertyPriority('position')
    const positioned = position === 'fixed' || position === 'absolute'
    const instance = target === document.body
      ? OverlayScrollbars(target, options)
      : OverlayScrollbars({ target, elements: { viewport: target } }, {
          ...options,
          overflow: {
            x: target.dataset.scrollAxis === 'y' ? 'hidden' : 'scroll',
            y: target.dataset.scrollAxis === 'x' ? 'hidden' : 'scroll',
          },
        })
    // OverlayScrollbars' host rule uses position: relative. Preserve floating
    // viewports so dialog centering and sheet anchoring keep their reference frame.
    if (positioned) target.style.setProperty('position', position)
    const hasKeyboardFocus = () => target.matches(':focus-visible') || Boolean(target.querySelector(':focus-visible'))
    const focus = () => { if (hasKeyboardFocus()) instance.options({ scrollbars: { autoHide: 'never' } }) }
    const blur = () => queueMicrotask(() => {
      if (!hasKeyboardFocus()) instance.options({ scrollbars: { autoHide: 'leave' } })
    })
    target.addEventListener('focusin', focus)
    target.addEventListener('focusout', blur)
    active.set(target, { instance, dispose: () => {
      target.removeEventListener('focusin', focus)
      target.removeEventListener('focusout', blur)
      instance.destroy()
      if (positioned) {
        if (originalPosition) target.style.setProperty('position', originalPosition, originalPriority)
        else target.style.removeProperty('position')
      }
    } })
  }
  const scan = (root: Element) => {
    if (root instanceof HTMLElement && root.matches('[data-scroll-surface]')) connect(root)
    root.querySelectorAll<HTMLElement>('[data-scroll-surface]').forEach(connect)
  }
  connect(document.body)
  scan(document.body)
  const observer = new MutationObserver(records => {
    for (const record of records) for (const node of record.addedNodes) if (node instanceof Element) scan(node)
    for (const [target, { dispose }] of active) if (!target.isConnected) { dispose(); active.delete(target) }
  })
  observer.observe(document.body, { childList: true, subtree: true })
  return () => { observer.disconnect(); active.forEach(item => item.dispose()); active.clear() }
}
