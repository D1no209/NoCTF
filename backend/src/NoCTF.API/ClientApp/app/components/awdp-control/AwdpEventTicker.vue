<script setup lang="ts">
import { Activity } from '@lucide/vue'
import type { AwdpResolvedControlEvent } from '~/utils/awdp-control-screen'

const props = defineProps<{ events: readonly AwdpResolvedControlEvent[] }>()
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
  const action = event.action === 'attack' ? t('攻击') : t('防御')
  const outcome = event.outcome === 'success' ? t('成功') : t('失败')
  return `${event.teamName} · ${event.challengeTitle} · ${action}${outcome}`
}

function eventTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleTimeString(undefined, { hour12: false })
}
</script>

<template>
  <section class="awdp-ticker hud-panel" :aria-label="$t('战况速览')">
    <div class="ticker-label"><Activity /><span>{{ $t('战况速览') }}</span><b>LIVE FEED</b></div>
    <div
      ref="viewport"
      class="ticker-viewport"
      @mouseenter="paused = true"
      @mouseleave="paused = false"
      @focusin="paused = true"
      @focusout="paused = false"
    >
      <div v-if="events.length" class="ticker-track" :style="{ transform: `translate3d(-${offset}px,0,0)` }">
        <div ref="group" class="ticker-group">
          <article v-for="event in events" :key="event.id" :class="['ticker-card', event.action, event.outcome]">
            <time>{{ eventTime(event.occurredAt) }}</time><span>{{ eventText(event) }}</span>
          </article>
        </div>
        <div class="ticker-group" aria-hidden="true">
          <article v-for="event in events" :key="`clone-${event.id}`" :class="['ticker-card', event.action, event.outcome]">
            <time>{{ eventTime(event.occurredAt) }}</time><span>{{ eventText(event) }}</span>
          </article>
        </div>
      </div>
      <div v-else class="ticker-empty">{{ $t('等待赛事操作') }}</div>
    </div>
  </section>
</template>

<style scoped>
.awdp-ticker{display:grid;grid-template-columns:8rem minmax(0,1fr);min-width:0;overflow:hidden}.ticker-label{display:flex;flex-direction:column;align-items:center;justify-content:center;border-right:1px solid rgba(0,188,255,.25);background:linear-gradient(90deg,rgba(0,90,126,.12),transparent);color:#65d9ff}.ticker-label svg{width:1.25rem;height:1.25rem}.ticker-label span{margin-top:.25rem;font:800 .78rem var(--awdp-display);letter-spacing:.16em}.ticker-label b{margin-top:.15rem;font:600 .52rem var(--awdp-mono);letter-spacing:.18em;color:#476e82}.ticker-viewport{overflow:hidden;min-width:0;outline:none}.ticker-track{display:flex;width:max-content;height:100%;will-change:transform}.ticker-group{display:flex;align-items:center;gap:.65rem;padding:.55rem .65rem}.ticker-card{position:relative;display:grid;grid-template-columns:auto auto;align-content:center;column-gap:.65rem;width:15.5rem;height:3.5rem;padding:.5rem .85rem;clip-path:polygon(.55rem 0,100% 0,100% calc(100% - .55rem),calc(100% - .55rem) 100%,0 100%,0 .55rem);border:1px solid var(--tone);background:linear-gradient(110deg,color-mix(in srgb,var(--tone) 14%,#02070d),#03080e 72%);box-shadow:inset 0 0 22px color-mix(in srgb,var(--tone) 7%,transparent)}.ticker-card.attack{--tone:#ff4338}.ticker-card.defense{--tone:#00d8c3}.ticker-card.failure{--tone:#ff7a18}.ticker-card.pending{--tone:#8b68ff}.ticker-card time{font:600 .56rem var(--awdp-mono);color:var(--tone)}.ticker-card span{font:650 .69rem var(--awdp-mono);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:#c0d4df}.ticker-empty{height:100%;display:grid;place-items:center;font:600 .72rem var(--awdp-mono);letter-spacing:.16em;color:#3d6478}
</style>
