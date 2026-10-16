import { nextTick } from 'vue'

/**
 * 明暗切换与壁纸切换共用的分层动效:
 *
 *   背景图 → 背景切换动效 → 组件 → 组件动效
 *
 * - 背景切换动效由「幽灵层」承担:切换前把当前背景层克隆成组件之下的固定层
 *   (内联冻结旧主题的背景色、壁纸与模糊变量),由它自上而下移出,露出实时新背景。
 * - 组件动效由组件自己承担:切换窗口内为实时组件打开颜色/阴影过渡,并按组件在
 *   视口中的纵向位置错峰 —— 扫描自上而下推进,因此"按扫描进度延迟"等价于
 *   "按扫描边界联动":卡片在被扫到的瞬间才切换填充,之前保持旧填充压在旧背景、
 *   之后是新填充压在新背景,半透明卡片不会压出中间灰。
 * - 全程不使用 View Transitions 快照:快照会把组件压成图层,卡片 backdrop-filter
 *   失去背景采样(发灰/虚化丢失),且切换期间组件不可交互。
 */

type SwitchLayer = 'theme' | 'wallpaper'

/** 背景自上而下扫描揭示的时长。 */
export const switchBackgroundDurationMs = 460
/** 单个组件自身的颜色过渡时长(扫描负责按位置排期)。 */
export const switchComponentDurationMs = 180
export const switchTransitionDeadlineMs = 1_500

const BACKGROUND_GHOST_ATTRIBUTE = 'wallpaperGhost'
const BACKGROUND_VARIABLES = ['--background', '--page-wallpaper-image', '--page-wallpaper-blur'] as const
/** 与 CSS 扫描关键帧同一条缓动曲线。 */
const SCAN_EASING = { x1: 0.4, y1: 0, x2: 0.2, y2: 1 }
/** 需要与扫描错峰的大面积半透明表面;小控件保持统一过渡即可。 */
const STAGGERED_SURFACE_SELECTOR = "[data-slot='card'], .card-surface, [data-slot='topbar-capsule'], [data-slot='topbar-frame']"

let switchInProgress = false
const staggeredSurfaces: HTMLElement[] = []

function bezierAxis(t: number, p1: number, p2: number): number {
  const inverse = 1 - t
  return 3 * inverse * inverse * t * p1 + 3 * inverse * t * t * p2 + t * t * t
}

/** 反解缓动曲线:扫描推进到 offset 比例时对应的时间比例。 */
function scanTimeForOffset(offset: number): number {
  const target = Math.min(1, Math.max(0, offset))
  let low = 0
  let high = 1
  for (let step = 0; step < 24; step += 1) {
    const middle = (low + high) / 2
    if (bezierAxis(middle, SCAN_EASING.x1, SCAN_EASING.x2) < target) low = middle
    else high = middle
  }
  return (low + high) / 2
}

/** 按视口纵向位置给大面积表面排期,使组件与扫描边界联动。 */
function staggerComponentReveal(): void {
  const viewportHeight = window.innerHeight || 1
  for (const element of document.querySelectorAll<HTMLElement>(STAGGERED_SURFACE_SELECTOR)) {
    const rect = element.getBoundingClientRect()
    if (rect.bottom < 0 || rect.top > viewportHeight) continue
    const reachedAt = scanTimeForOffset(rect.top / viewportHeight) * switchBackgroundDurationMs
    const delay = Math.max(0, Math.round(reachedAt - switchComponentDurationMs / 2))
    element.style.setProperty('transition-delay', `${delay}ms`)
    staggeredSurfaces.push(element)
  }
}

function clearComponentReveal(): void {
  for (const element of staggeredSurfaces) element.style.removeProperty('transition-delay')
  staggeredSurfaces.length = 0
}

function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/** 冻结当前背景观感,生成位于组件之下的幽灵层。 */
function createBackgroundGhost(): HTMLElement | null {
  const wallpaper = document.querySelector<HTMLElement>('[data-page-wallpaper="true"]')
  if (!wallpaper) return null

  const computed = window.getComputedStyle(wallpaper)
  const ghost = wallpaper.cloneNode(true) as HTMLElement
  for (const name of BACKGROUND_VARIABLES) {
    const value = computed.getPropertyValue(name).trim()
    if (value) ghost.style.setProperty(name, value)
  }
  ghost.setAttribute('aria-hidden', 'true')
  ghost.dataset[BACKGROUND_GHOST_ATTRIBUTE] = ''
  // 插到背景层之后:同为负层级,幽灵层盖住旧背景却仍在组件之下。
  wallpaper.after(ghost)
  return ghost
}

function waitForGhost(ghost: HTMLElement | null): Promise<void> {
  if (!ghost) return Promise.resolve()
  return new Promise<void>((resolve) => {
    let settled = false
    const finish = () => {
      if (settled) return
      settled = true
      clearTimeout(deadline)
      resolve()
    }
    const deadline = setTimeout(finish, switchTransitionDeadlineMs)
    ghost.addEventListener('animationend', finish, { once: true })
    // 元素被移除或动画未触发时也不能卡住切换状态。
    window.setTimeout(finish, switchBackgroundDurationMs + 120)
  })
}

/** 运行分层切换:背景由幽灵层揭示,组件在实时 DOM 上完成自身动效。 */
export async function runSwitchTransition(
  layer: SwitchLayer,
  update: () => void | Promise<void>,
): Promise<void> {
  if (typeof document === 'undefined' || typeof window === 'undefined') {
    await update()
    return
  }

  const datasetKey = layer === 'theme' ? 'themeTransition' : 'wallpaperTransition'
  if (prefersReducedMotion() || switchInProgress) {
    await update()
    await nextTick()
    return
  }

  switchInProgress = true
  const ghost = createBackgroundGhost()
  // 只有真的进行扫描时才错峰:没有背景层的路由(首页)是同步切换。
  if (ghost) staggerComponentReveal()
  try {
    document.documentElement.dataset[datasetKey] = ''
    await update()
    await nextTick()
    await waitForGhost(ghost)
  }
  finally {
    clearComponentReveal()
    ghost?.remove()
    delete document.documentElement.dataset[datasetKey]
    switchInProgress = false
  }
}
