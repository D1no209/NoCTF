import { onBeforeUnmount, watch, type Ref } from 'vue'

export function scrollDockOffset(introHeight: number, scrollTop: number): number {
  return Math.max(0, introHeight - Math.max(0, scrollTop))
}

/** Follow upward travel directly; ease back into the document flow on reverse travel. */
export function advanceScrollDock(current: number, target: number, elapsedMs: number, reduced = false, tolerance = 0.25): number {
  if (reduced || target <= current || Math.abs(target - current) < tolerance) return target
  return current + (target - current) * (1 - Math.exp(-Math.max(0, elapsedMs) / 140))
}

export interface ScrollDockItem { x: number; y: number; width: number; height: number; scale: number; flexible?: boolean }

export function compactScrollDock(items: readonly ScrollDockItem[], width: number) {
  const padding = 12
  const gap = 12
  let x = padding
  let y = padding
  let rowHeight = 0
  const flexibleWidth = width - padding * 2 - gap * Math.max(0, items.length - 1)
    - items.filter(item => !item.flexible).reduce((sum, item) => sum + item.width * item.scale, 0)
  const positions = items.map(item => {
    const scale = Math.min(item.scale, (width - padding * 2) / Math.max(1, item.width))
    const itemWidth = item.flexible && flexibleWidth >= 100
      ? Math.min(item.width * scale, flexibleWidth)
      : item.width * scale
    const itemHeight = item.height * scale
    if (x > padding && x + itemWidth > width - padding) {
      x = padding
      y += rowHeight + gap
      rowHeight = 0
    }
    const position = { x, y, scale, width: itemWidth }
    x += itemWidth + gap
    rowHeight = Math.max(rowHeight, itemHeight)
    return position
  })
  return { positions, height: y + rowHeight + padding }
}

export function useScrollDockMotion(root: Ref<HTMLElement | null>) {
  let dispose: (() => void) | undefined
  watch(root, element => {
    dispose?.()
    if (!element) return
    const viewport = element.querySelector<HTMLElement>('[data-scroll-dock-viewport]')
    const intro = element.querySelector<HTMLElement>('[data-scroll-dock-intro]')
    const dock = element.querySelector<HTMLElement>('[data-scroll-dock-header]')
    const body = element.querySelector<HTMLElement>('[data-scroll-dock-body]')
    if (!viewport || !intro || !dock || !body) return
    const items = [...dock.querySelectorAll<HTMLElement>('[data-scroll-dock-item]')]
      .sort((a, b) => Number(a.dataset.scrollDockOrder ?? 0) - Number(b.dataset.scrollDockOrder ?? 0))
    const secondary = [...dock.querySelectorAll<HTMLElement>('[data-scroll-dock-secondary]')]
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    let introHeight = intro.offsetHeight
    let displayed = 0
    let naturalHeight = dock.offsetHeight
    let geometry: ScrollDockItem[] = []
    let compact = compactScrollDock([], dock.clientWidth)
    let frame = 0
    let previousTime = 0
    let disposed = false
    function paint(time: number) {
      frame = 0
      const progress = media.matches ? Number(viewport!.scrollTop >= introHeight)
        : introHeight > 0 ? Math.min(1, Math.max(0, viewport!.scrollTop) / introHeight) : 1
      // The inverse progress makes reverse travel use the same bounded easing.
      displayed = 1 - advanceScrollDock(1 - displayed, 1 - progress,
        previousTime ? Math.min(64, time - previousTime) : 16, media.matches, 0.001)
      previousTime = time
      dock!.style.setProperty('--scroll-dock-progress', displayed.toFixed(4))
      const height = naturalHeight + (compact.height - naturalHeight) * displayed
      dock!.style.height = `${height}px`
      viewport!.style.scrollPaddingTop = `${height}px`
      body!.style.clipPath = `inset(${Math.max(0, viewport!.scrollTop - introHeight)}px 0 0)`
      element!.style.setProperty('--scroll-dock-backdrop-height', `${introHeight + height}px`)
      items.forEach((item, index) => {
        const source = geometry[index]
        const target = compact.positions[index]
        if (!source || !target) return
        const x = (target.x - source.x) * displayed
        const y = (target.y - source.y) * displayed
        const scale = 1 + (target.scale - 1) * displayed
        item.style.setProperty('--scroll-dock-x', `${x.toFixed(2)}px`)
        item.style.setProperty('--scroll-dock-y', `${y.toFixed(2)}px`)
        item.style.setProperty('--scroll-dock-scale', scale.toFixed(4))
        if (source.flexible)
          item.style.setProperty('--scroll-dock-width', `${source.width + (target.width / target.scale - source.width) * displayed}px`)
      })
      secondary.forEach(item => {
        item.style.setProperty('--scroll-dock-secondary-opacity', Math.pow(1 - displayed, 3).toFixed(4))
        item.inert = displayed > 0.95
        if (displayed > 0.95) item.setAttribute('aria-hidden', 'true')
        else item.removeAttribute('aria-hidden')
      })
      dock!.dataset.pinned = viewport!.scrollTop >= introHeight ? 'true' : 'false'
      if (Math.abs(displayed - progress) >= 0.001) frame = requestAnimationFrame(paint)
      else previousTime = 0
    }
    function schedule() {
      if (!disposed && !frame) frame = requestAnimationFrame(paint)
    }
    function measure() {
      dock!.style.removeProperty('height')
      items.forEach(item => {
        item.style.setProperty('--scroll-dock-measuring', 'none')
        item.style.removeProperty('--scroll-dock-width')
      })
      introHeight = intro!.offsetHeight
      naturalHeight = dock!.offsetHeight
      element!.style.setProperty('--scroll-dock-backdrop-natural-height', `${introHeight + naturalHeight}px`)
      const bounds = dock!.getBoundingClientRect()
      geometry = items.map(item => {
        const rect = item.getBoundingClientRect()
        return { x: rect.left - bounds.left, y: rect.top - bounds.top, width: rect.width, height: rect.height,
          scale: Number(item.dataset.scrollDockScale ?? 0.82), flexible: item.hasAttribute('data-scroll-dock-flex') }
      })
      compact = compactScrollDock(geometry, dock!.clientWidth)
      items.forEach(item => item.style.removeProperty('--scroll-dock-measuring'))
      body!.style.minHeight = `${Math.max(0, viewport!.clientHeight - compact.height)}px`
      displayed = introHeight > 0 ? Math.min(1, Math.max(0, viewport!.scrollTop) / introHeight) : 1
      schedule()
    }
    const resize = new ResizeObserver(measure)
    resize.observe(intro)
    resize.observe(viewport)
    const mutation = new MutationObserver(measure)
    mutation.observe(dock, { childList: true, characterData: true, subtree: true })
    viewport.addEventListener('scroll', schedule, { passive: true })
    media.addEventListener('change', schedule)
    measure()
    dispose = () => {
      disposed = true
      cancelAnimationFrame(frame)
      resize.disconnect()
      mutation.disconnect()
      viewport.removeEventListener('scroll', schedule)
      media.removeEventListener('change', schedule)
      dock.style.removeProperty('--scroll-dock-progress')
      dock.style.removeProperty('height')
      body.style.removeProperty('min-height')
      body.style.removeProperty('clip-path')
      viewport.style.removeProperty('scroll-padding-top')
      element.style.removeProperty('--scroll-dock-backdrop-height')
      element.style.removeProperty('--scroll-dock-backdrop-natural-height')
      items.forEach(item => {
        item.style.removeProperty('--scroll-dock-x')
        item.style.removeProperty('--scroll-dock-y')
        item.style.removeProperty('--scroll-dock-scale')
        item.style.removeProperty('--scroll-dock-width')
      })
      secondary.forEach(item => {
        item.style.removeProperty('--scroll-dock-secondary-opacity')
        item.inert = false
        item.removeAttribute('aria-hidden')
      })
      delete dock.dataset.pinned
    }
  }, { flush: 'post' })
  onBeforeUnmount(() => dispose?.())
}
