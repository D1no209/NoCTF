import { nextTick } from 'vue'

/**
 * 明暗切换与壁纸切换共用的分层动效:
 *
 *   背景图 → 背景切换动效 → 组件 → 组件动效
 *
 * - 背景切换动效由「幽灵层」承担:切换前把当前背景层克隆成组件之下的固定层
 *   (内联冻结旧主题的背景色、壁纸与模糊变量),由它自上而下移出,露出实时新背景。
 * - 组件动效由组件自己承担:切换窗口内为实时组件打开颜色/阴影过渡,与背景同时进行。
 * - 全程不使用 View Transitions 快照:快照会把组件压成图层,卡片 backdrop-filter
 *   失去背景采样(发灰/虚化丢失),且切换期间组件不可交互。
 */

type SwitchLayer = 'theme' | 'wallpaper'

/** 背景自上而下扫描揭示的时长;组件颜色过渡与之同时长、同缓动。 */
export const switchBackgroundDurationMs = 460
export const switchComponentDurationMs = 460
export const switchTransitionDeadlineMs = 1_500

const BACKGROUND_GHOST_ATTRIBUTE = 'wallpaperGhost'
const BACKGROUND_VARIABLES = ['--background', '--page-wallpaper-image', '--page-wallpaper-blur'] as const

let switchInProgress = false

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
  try {
    document.documentElement.dataset[datasetKey] = ''
    await update()
    await nextTick()
    await waitForGhost(ghost)
  }
  finally {
    ghost?.remove()
    delete document.documentElement.dataset[datasetKey]
    switchInProgress = false
  }
}
