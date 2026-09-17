<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpEventTickerViewState } from '~/features/awdp-control/useAwdpEventTicker'

const viewProps = defineProps<{ state: AwdpEventTickerViewState }>()
const { Activity, eventText, eventTime, setViewportRef, setGroupRef, setTrackRef, events, onMouseenterPaused, onMouseleavePaused } = toRefs(viewProps.state)
</script>

<template>
  <section class="awdp-ticker hud-panel" :aria-label="$t('ui.battleFeed')">
    <div class="ticker-label"><Activity /><span>{{ $t('ui.battleFeed') }}</span><b>{{ $t('ui.liveFeed2') }}</b></div>
    <div
      :ref="setViewportRef"
      class="ticker-viewport"
      @mouseenter="onMouseenterPaused(true)"
      @mouseleave="onMouseleavePaused(false)"
      @focusin="onMouseenterPaused(true)"
      @focusout="onMouseleavePaused(false)"
    >
      <div v-if="events.length" :ref="setTrackRef" class="ticker-track">
        <div :ref="setGroupRef" class="ticker-group">
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
      <div v-else class="ticker-empty">{{ $t('ui.awaitingCompetitionActivity') }}</div>
    </div>
  </section>
</template>

<style scoped>
.awdp-ticker{display:grid;grid-template-columns:8rem minmax(0,1fr);min-width:0;overflow:hidden}.ticker-label{display:flex;flex-direction:column;align-items:center;justify-content:center;border-right:1px solid color-mix(in oklch, var(--primary) 25%, transparent);background:linear-gradient(90deg,color-mix(in oklch, var(--primary) 12%, transparent),transparent);color:var(--primary)}.ticker-label svg{width:1.25rem;height:1.25rem}.ticker-label span{margin-top:.25rem;font:800 .78rem var(--awdp-display);letter-spacing:.16em}.ticker-label b{margin-top:.15rem;font:600 .52rem var(--awdp-mono);letter-spacing:.18em;color:var(--primary)}.ticker-viewport{overflow:hidden;min-width:0;outline:none}.ticker-track{display:flex;width:max-content;height:100%;will-change:transform}.ticker-group{display:flex;align-items:center;gap:.65rem;padding:.55rem .65rem}.ticker-card{position:relative;display:grid;grid-template-columns:auto auto;align-content:center;column-gap:.65rem;width:15.5rem;height:3.5rem;padding:.5rem .85rem;clip-path:polygon(.55rem 0,100% 0,100% calc(100% - .55rem),calc(100% - .55rem) 100%,0 100%,0 .55rem);border:1px solid var(--tone);background:linear-gradient(110deg,color-mix(in srgb,var(--tone) 14%,var(--background)),var(--background) 72%);box-shadow:var(--panel-shadow)}.ticker-card.attack{--tone:var(--destructive)}.ticker-card.defense{--tone:var(--success)}.ticker-card.failure{--tone:var(--destructive)}.ticker-card.pending{--tone:var(--primary)}.ticker-card time{font:600 .56rem var(--awdp-mono);color:var(--tone)}.ticker-card span{font:650 .69rem var(--awdp-mono);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:var(--primary)}.ticker-empty{height:100%;display:grid;place-items:center;font:600 .72rem var(--awdp-mono);letter-spacing:.16em;color:var(--primary)}
</style>
