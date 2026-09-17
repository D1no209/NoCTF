import { nextTick } from 'vue'

export const localeLayoutMotionDuration = 500
export const maximumLocaleLayoutControls = 96
export const maximumLocaleLayoutCandidates = 256

const localeResizeSelector = [
  "[data-slot='button']",
  "[data-slot='action-button']",
  "[data-slot='badge']",
  "[data-slot='tabs-trigger']",
  "[data-slot='toggle-group-item']",
  "[data-slot='select-trigger']",
].join(',')

const activeAnimations = new WeakMap<HTMLElement, Animation>()

function isVisibleInViewport(rect: DOMRect): boolean {
  const viewportWidth = document.documentElement.clientWidth
  const viewportHeight = document.documentElement.clientHeight
  return rect.width > 0
    && rect.height > 0
    && rect.right >= 0
    && rect.bottom >= 0
    && rect.left <= viewportWidth
    && rect.top <= viewportHeight
}

function visibleControls(): Map<HTMLElement, number> {
  const widths = new Map<HTMLElement, number>()
  let inspected = 0
  for (const control of document.querySelectorAll<HTMLElement>(localeResizeSelector)) {
    inspected += 1
    if (inspected > maximumLocaleLayoutCandidates || widths.size >= maximumLocaleLayoutControls) break
    const rect = control.getBoundingClientRect()
    if (!isVisibleInViewport(rect)) continue
    widths.set(control, rect.width)
  }
  return widths
}

/** Smooth intrinsic-width changes caused by replacing localized control labels. */
export async function animateLocaleLayout(update: () => void): Promise<void> {
  if (!import.meta.client || matchMedia('(prefers-reduced-motion: reduce)').matches) {
    update()
    return
  }

  const widths = visibleControls()

  update()
  await nextTick()

  for (const [control, previousWidth] of widths) {
    if (!control.isConnected) continue
    const nextWidth = control.getBoundingClientRect().width
    if (Math.abs(nextWidth - previousWidth) < 0.5) continue

    activeAnimations.get(control)?.cancel()
    control.dataset.localeResizing = 'true'
    const animation = control.animate(
      [
        { inlineSize: `${previousWidth}px` },
        { inlineSize: `${nextWidth}px` },
      ],
      {
        duration: localeLayoutMotionDuration,
        easing: 'cubic-bezier(0.22, 1, 0.36, 1)',
      },
    )
    activeAnimations.set(control, animation)
    void animation.finished.catch(() => undefined).then(() => {
      if (activeAnimations.get(control) !== animation) return
      activeAnimations.delete(control)
      delete control.dataset.localeResizing
    })
  }
}
