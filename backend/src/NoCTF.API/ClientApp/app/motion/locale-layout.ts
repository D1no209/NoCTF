import { nextTick } from 'vue'

export const localeLayoutMotionDuration = 500

const localeResizeSelector = [
  "[data-slot='button']",
  "[data-slot='action-button']",
  "[data-slot='badge']",
  "[data-slot='tabs-trigger']",
  "[data-slot='toggle-group-item']",
  "[data-slot='select-trigger']",
].join(',')

const activeAnimations = new WeakMap<HTMLElement, Animation>()

/** Smooth intrinsic-width changes caused by replacing localized control labels. */
export async function animateLocaleLayout(update: () => void): Promise<void> {
  if (!import.meta.client || matchMedia('(prefers-reduced-motion: reduce)').matches) {
    update()
    return
  }

  const controls = [...document.querySelectorAll<HTMLElement>(localeResizeSelector)]
  const widths = new Map<HTMLElement, number>()
  for (const control of controls) {
    const width = control.getBoundingClientRect().width
    if (width > 0) widths.set(control, width)
  }

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
