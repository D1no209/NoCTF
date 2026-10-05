import { nextTick, proxyRefs, toRefs, watch } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Activity } from '@lucide/vue'
import type { AwdpResolvedControlEvent } from '../../utils/awdp-control-screen'

/** Owns state, effects and commands for AwdpEventTicker. */
export function useAwdpEventTicker(props: Readonly<{ events: readonly AwdpResolvedControlEvent[] }>) {
  const { t } = useLocale()

  const viewport = ref<HTMLElement | null>(null)

  const group = ref<HTMLElement | null>(null)

  const track = ref<HTMLElement | null>(null)

  const paused = ref(false)

  let animationFrame = 0

  let previousFrame = 0

  let groupWidth = 0

  let offset = 0

  let mounted = false

  let resizeObserver: ResizeObserver | undefined

  let reducedMotion: MediaQueryList | undefined

  function applyOffset(): void {
    track.value?.style.setProperty('transform', `translate3d(-${offset.toFixed(2)}px,0,0)`)
  }

  function measureGroup(): void {
    groupWidth = group.value?.getBoundingClientRect().width ?? 0
    offset = groupWidth > 0 ? offset % groupWidth : 0
    applyOffset()
  }

  function canAnimate(): boolean {
    return mounted
      && !paused.value
      && !document.hidden
      && !reducedMotion?.matches
      && groupWidth > 0
      && track.value !== null
  }

  function stopAnimation(): void {
    if (animationFrame) cancelAnimationFrame(animationFrame)
    animationFrame = 0
    previousFrame = 0
  }

  function scheduleAnimation(): void {
    if (!animationFrame && canAnimate()) animationFrame = requestAnimationFrame(tick)
  }

  function syncAnimation(): void {
    if (canAnimate()) scheduleAnimation()
    else stopAnimation()
  }

  function tick(timestamp: number): void {
    animationFrame = 0
    if (!canAnimate()) {
      previousFrame = 0
      return
    }
    const delta = previousFrame ? Math.min(32, timestamp - previousFrame) : 0
    previousFrame = timestamp
    offset = (offset + delta * 0.055) % groupWidth
    applyOffset()
    scheduleAnimation()
  }

  function observeGroup(): void {
    resizeObserver?.disconnect()
    if (group.value) resizeObserver?.observe(group.value)
    measureGroup()
    syncAnimation()
  }

  function onVisibilityChange(): void {
    syncAnimation()
  }

  function onMotionPreferenceChange(): void {
    if (reducedMotion?.matches) {
      offset = 0
      applyOffset()
    }
    syncAnimation()
  }

  watch(paused, syncAnimation)

  watch(
    () => props.events.map(event => event.id).join('|'),
    async () => {
      await nextTick()
      measureGroup()
      syncAnimation()
    },
    { flush: 'post' },
  )

  onMounted(() => {
    mounted = true
    reducedMotion = matchMedia('(prefers-reduced-motion: reduce)')
    reducedMotion.addEventListener('change', onMotionPreferenceChange)
    resizeObserver = new ResizeObserver(() => {
      measureGroup()
      syncAnimation()
    })
    document.addEventListener('visibilitychange', onVisibilityChange)
    observeGroup()
  })

  onUnmounted(() => {
    mounted = false
    stopAnimation()
    resizeObserver?.disconnect()
    reducedMotion?.removeEventListener('change', onMotionPreferenceChange)
    document.removeEventListener('visibilitychange', onVisibilityChange)
  })

  function eventText(event: AwdpResolvedControlEvent): string {
    const action = event.action === 'attack' ? t("competitions.label.attack") : t("competitions.label.defense")
    const outcome = event.outcome === 'success' ? t("competitions.label.success") : t("common.error.failed")
    return `${event.teamName} · ${event.challengeTitle} · ${action}${outcome}`
  }

  function eventTime(value: string): string {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleTimeString(undefined, { hour12: false })
  }

  function setViewportRef(element: Element | ComponentPublicInstance | null) { viewport.value = element as typeof viewport.value }

  function setGroupRef(element: Element | ComponentPublicInstance | null) {
    group.value = element as typeof group.value
    if (mounted) observeGroup()
  }

  function setTrackRef(element: Element | ComponentPublicInstance | null) {
    track.value = element as typeof track.value
    applyOffset()
    syncAnimation()
  }

  const viewBindings = {
      ...toRefs(props),
      Activity,
      t,
      viewport,
      group,
      track,
      paused,
      eventText,
      eventTime,
      setViewportRef,
      setGroupRef,
      setTrackRef,
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
