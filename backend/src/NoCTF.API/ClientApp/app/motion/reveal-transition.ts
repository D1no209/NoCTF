import { nextTick } from 'vue'

/**
 * 明暗切换与壁纸切换共用的统一动效:
 *
 *   背景图 → 背景切换动效 → 组件 → 组件动效 → 顶层扫描
 *
 * - 主题与壁纸在同一帧原子切换:组件与背景始终成对变化,半透明卡片不会
 *   压出中间灰,也不需要把组件遮罩起来。
 * - 组件动效由组件自己完成(motion.css 中的 180ms 颜色过渡),保持实时 DOM。
 * - 扫描动效提升到顶层:一个 pointer-events:none 的固定光带在**所有组件之上**
 *   自上而下扫过,只做 transform 合成 —— 不重绘、不拦截交互、不产生快照。
 */

type SwitchLayer = 'theme' | 'wallpaper'

/** 顶层扫描光带的时长。 */
export const switchScanDurationMs = 460
/** 组件自身颜色过渡时长。 */
export const switchComponentDurationMs = 180
export const switchTransitionDeadlineMs = 1_500

const SCAN_VEIL_ATTRIBUTE = 'switchScanVeil'

let switchInProgress = false

function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/** 顶层扫描光带:覆盖在组件之上,只承担扫描动感。 */
function createScanVeil(): HTMLElement {
  const veil = document.createElement('div')
  veil.setAttribute('aria-hidden', 'true')
  veil.dataset[SCAN_VEIL_ATTRIBUTE] = ''
  document.body.appendChild(veil)
  return veil
}

function waitForVeil(veil: HTMLElement): Promise<void> {
  return new Promise<void>((resolve) => {
    let settled = false
    const finish = () => {
      if (settled) return
      settled = true
      clearTimeout(deadline)
      resolve()
    }
    const deadline = setTimeout(finish, switchTransitionDeadlineMs)
    veil.addEventListener('animationend', finish, { once: true })
    // 动画未触发(元素被移除等)也不能把切换状态卡住。
    window.setTimeout(finish, switchScanDurationMs + 120)
  })
}

/** 运行统一切换:主题原子切换 + 组件自身过渡 + 顶层扫描光带。 */
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
  const veil = createScanVeil()
  try {
    document.documentElement.dataset[datasetKey] = ''
    await update()
    await nextTick()
    await waitForVeil(veil)
  }
  finally {
    veil.remove()
    delete document.documentElement.dataset[datasetKey]
    switchInProgress = false
  }
}
