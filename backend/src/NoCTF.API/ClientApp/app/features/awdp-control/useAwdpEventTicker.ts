import { proxyRefs } from 'vue'
import { toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Activity } from '@lucide/vue'
import type { AwdpResolvedControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpEventTicker. */
export function useAwdpEventTicker(props: Readonly<{ events: readonly AwdpResolvedControlEvent[] }>) {
  const { t } = useLocale()

  const viewport = ref<HTMLElement | null>(null)

  const group = ref<HTMLElement | null>(null)

  const paused = ref(false)

  const offset = ref(0)

  let animationFrame = 0

  let previousFrame = 0

  function tick(timestamp: number): void {
    const width = group.value?.offsetWidth ?? 0
    const delta = previousFrame ? Math.min(32, timestamp - previousFrame) : 0
    previousFrame = timestamp
    if (!paused.value && width > 0) offset.value = (offset.value + delta * 0.055) % width
    animationFrame = requestAnimationFrame(tick)
  }

  onMounted(() => { animationFrame = requestAnimationFrame(tick) })

  onUnmounted(() => cancelAnimationFrame(animationFrame))

  function eventText(event: AwdpResolvedControlEvent): string {
    const action = event.action === 'attack' ? t("ui.attack") : t("ui.defense")
    const outcome = event.outcome === 'success' ? t("ui.success") : t("ui.failed")
    return `${event.teamName} · ${event.challengeTitle} · ${action}${outcome}`
  }

  function eventTime(value: string): string {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleTimeString(undefined, { hour12: false })
  }

  function setViewportRef(element: Element | ComponentPublicInstance | null) { viewport.value = element as typeof viewport.value }

  function setGroupRef(element: Element | ComponentPublicInstance | null) { group.value = element as typeof group.value }

  const viewBindings = {
      ...toRefs(props),
      Activity,
      t,
      viewport,
      group,
      paused,
      offset,
      eventText,
      eventTime,
      setViewportRef,
      setGroupRef
    }
  const viewState = proxyRefs(viewBindings)

  function onMouseenterPaused(value: typeof viewState.paused) {
    viewState.paused = value
  }

  function onMouseleavePaused(value: typeof viewState.paused) {
    viewState.paused = value
  }

  return { ...viewBindings, onMouseenterPaused, onMouseleavePaused }
}

export type AwdpEventTickerViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpEventTicker>>>
