<script setup lang="ts">
import type { AwdpChallengeStatus, AwdpScreenEvent, AwdpTeamScore } from '@/types/awdpScreen'
import { Activity, RadioTower, RotateCw, Shield, Swords, TriangleAlert } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  teams: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  events: AwdpScreenEvent[]
  currentRound: number
}>()

type SenseMode = 'attack' | 'defense' | 'service'
type SenseResult = 'success' | 'failed' | 'error'

interface SenseEvent {
  id: string
  mode: SenseMode
  result: SenseResult
  label: string
  teamId?: string
  teamName: string
  challengeId?: string
  challengeName: string
  round: number
  createdAt: string
}

interface NodePoint {
  id: string
  label: string
  sub?: string
  x: number
  y: number
  active: boolean
}

const ROTATE_MS = 3_400
const activeIndex = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const senseEvents = computed(() => props.events
  .map(toSenseEvent)
  .filter((event): event is SenseEvent => Boolean(event))
  .slice(0, 24))

const currentEvent = computed(() => senseEvents.value[activeIndex.value] ?? null)
const currentRoundLabel = computed(() => currentEvent.value?.round || props.currentRound)
const panelClass = computed(() => currentEvent.value ? `sense-${currentEvent.value.mode}-${currentEvent.value.result}` : 'sense-idle')

const teamNodes = computed<NodePoint[]>(() => {
  const teams = props.teams.slice(0, 8)
  const step = teams.length > 1 ? 300 / (teams.length - 1) : 0
  return teams.map((team, index) => ({
    id: team.teamId,
    label: team.teamName,
    sub: `#${team.rank}`,
    x: 78,
    y: 64 + step * index,
    active: team.teamId === currentEvent.value?.teamId || team.teamName === currentEvent.value?.teamName,
  }))
})

const challengeNodes = computed<NodePoint[]>(() => {
  const challenges = props.challenges.slice(0, 7)
  const step = challenges.length > 1 ? 292 / (challenges.length - 1) : 0
  return challenges.map((challenge, index) => ({
    id: challenge.challengeId,
    label: challenge.challengeName,
    sub: challenge.category.toUpperCase(),
    x: 722,
    y: 68 + step * index,
    active: challenge.challengeId === currentEvent.value?.challengeId || challenge.challengeName === currentEvent.value?.challengeName,
  }))
})

watch(senseEvents, (events) => {
  activeIndex.value = Math.min(activeIndex.value, Math.max(0, events.length - 1))
})

watch(() => senseEvents.value[0]?.id, () => {
  activeIndex.value = 0
})

onMounted(() => {
  rotateTimer = setInterval(() => {
    if (senseEvents.value.length > 1)
      activeIndex.value = (activeIndex.value + 1) % senseEvents.value.length
  }, ROTATE_MS)
})

onUnmounted(() => {
  if (rotateTimer)
    clearInterval(rotateTimer)
})

function toSenseEvent(event: AwdpScreenEvent): SenseEvent | null {
  if (event.type === 'ATTACK_ACCEPTED') {
    return buildSenseEvent(event, 'attack', 'success', 'ATTACK SUCCESS')
  }
  if (event.type === 'ATTACK_REJECTED') {
    return buildSenseEvent(event, 'attack', 'failed', 'ATTACK FAILED')
  }
  if (event.type === 'DEFENSE_CHECK_PASSED') {
    return buildSenseEvent(event, 'defense', 'success', 'DEFENSE SUCCESS')
  }
  if (event.type === 'DEFENSE_CHECK_FAILED') {
    return buildSenseEvent(event, 'defense', 'failed', 'DEFENSE FAILED')
  }
  if (event.type === 'SERVICE_ERROR') {
    return buildSenseEvent(event, 'service', 'error', 'SERVICE ERROR')
  }
  return null
}

function buildSenseEvent(
  event: AwdpScreenEvent,
  mode: SenseMode,
  result: SenseResult,
  label: string,
): SenseEvent {
  return {
    id: event.id,
    mode,
    result,
    label,
    teamId: event.teamId,
    teamName: event.teamName ?? 'Team',
    challengeId: event.challengeId,
    challengeName: event.challengeName ?? 'Service',
    round: event.round || props.currentRound,
    createdAt: event.createdAt,
  }
}

function modeIcon(mode?: SenseMode) {
  if (mode === 'attack')
    return Swords
  if (mode === 'defense')
    return Shield
  if (mode === 'service')
    return TriangleAlert
  return RadioTower
}
</script>

<template>
  <section class="awdp-panel sense-panel" :class="panelClass">
    <div class="sense-header">
      <div>
        <h2 class="text-sm font-semibold text-slate-100">
          Center realtime awareness
        </h2>
        <p class="text-xs text-slate-500">
          Event result only, scores settle by round
        </p>
      </div>
      <div class="sense-header-state">
        <component :is="modeIcon(currentEvent?.mode)" class="size-4" />
        <span>{{ currentEvent?.label ?? 'WAITING EVENT' }}</span>
      </div>
    </div>

    <div class="sense-stage">
      <div class="sense-grid" />
      <svg viewBox="0 0 800 420" class="sense-canvas" aria-hidden="true">
        <g class="sense-lanes">
          <line x1="102" y1="210" x2="318" y2="210" />
          <line x1="482" y1="210" x2="698" y2="210" />
        </g>

        <g v-if="currentEvent?.mode === 'attack'" class="attack-path">
          <path class="attack-beam attack-beam-in" d="M105 210 C175 172 245 172 330 210" />
          <path
            v-if="currentEvent.result === 'success'"
            class="attack-beam attack-beam-out beam-success"
            d="M470 210 C560 172 625 172 704 210"
          />
          <g v-else class="beam-fragments">
            <path d="M470 210 C520 188 552 184 585 192" />
            <path d="M608 199 C636 204 655 208 676 214" />
          </g>
        </g>

        <g v-if="currentEvent?.mode === 'defense'" class="defense-path">
          <path class="defense-wave" d="M110 210 C202 244 254 244 338 210" />
          <path
            class="defense-wave"
            :class="currentEvent.result === 'success' ? 'wave-complete' : 'wave-broken'"
            d="M462 210 C548 244 612 244 694 210"
          />
        </g>

        <g v-if="currentEvent?.mode === 'service'" class="service-path">
          <path class="service-alert-line" d="M112 210 C210 150 287 150 356 194" />
          <path class="service-alert-line service-alert-line-right" d="M444 226 C536 270 622 268 694 210" />
        </g>

        <g v-for="node in teamNodes" :key="node.id" class="sense-node" :class="{ active: node.active }">
          <circle :cx="node.x" :cy="node.y" r="13" />
          <circle :cx="node.x" :cy="node.y" r="22" class="node-ring" />
          <text :x="node.x + 28" :y="node.y - 3" class="node-label">{{ node.label }}</text>
          <text :x="node.x + 28" :y="node.y + 13" class="node-sub">{{ node.sub }}</text>
        </g>

        <g v-for="node in challengeNodes" :key="node.id" class="sense-node challenge-node" :class="{ active: node.active }">
          <circle :cx="node.x" :cy="node.y" r="13" />
          <circle :cx="node.x" :cy="node.y" r="22" class="node-ring" />
          <text :x="node.x - 28" :y="node.y - 3" text-anchor="end" class="node-label">{{ node.label }}</text>
          <text :x="node.x - 28" :y="node.y + 13" text-anchor="end" class="node-sub">{{ node.sub }}</text>
        </g>

        <g class="impact-zone" :class="{ active: currentEvent }">
          <circle cx="706" cy="210" r="30" />
          <circle cx="706" cy="210" r="46" />
        </g>

        <g class="core-rings">
          <circle cx="400" cy="210" r="74" />
          <circle cx="400" cy="210" r="100" />
          <circle cx="400" cy="210" r="128" />
        </g>
      </svg>

      <div class="sense-core">
        <div class="core-orbit" />
        <div class="core-card">
          <div class="core-round">
            ROUND {{ currentRoundLabel }}
          </div>
          <div class="core-result">
            {{ currentEvent?.label ?? 'WAITING EVENT' }}
          </div>
          <div class="core-route">
            {{ currentEvent?.teamName ?? '-' }} → {{ currentEvent?.challengeName ?? '-' }}
          </div>
        </div>
      </div>

      <div v-if="!currentEvent" class="sense-empty">
        Waiting for real attack, defense, or service events.
      </div>
    </div>

    <div class="sense-footer">
      <span class="inline-flex items-center gap-2">
        <Activity class="size-3.5" />
        Result playback
      </span>
      <span class="inline-flex items-center gap-2">
        <RotateCw class="size-3.5" />
        Live event cycle
      </span>
    </div>
  </section>
</template>

<style scoped>
.sense-panel {
  --sense-border: color-mix(in oklch, var(--sidebar-foreground) 14%, transparent);
  --sense-border-strong: color-mix(in oklch, var(--sidebar-foreground) 24%, transparent);
  --sense-muted: color-mix(in oklch, var(--sidebar-foreground) 56%, transparent);
  --sense-muted-soft: color-mix(in oklch, var(--sidebar-foreground) 38%, transparent);
  --sense-text: var(--sidebar-foreground);
  --sense-panel-bg: color-mix(in oklch, var(--sidebar) 88%, black);
  --sense-panel-bg-soft: color-mix(in oklch, var(--sidebar) 72%, transparent);
  --sense-node: color-mix(in oklch, var(--sidebar-foreground) 22%, var(--sidebar));
  --sense-attack: var(--chart-2);
  --sense-defense: var(--chart-2);
  --sense-failed: var(--chart-1);
  --sense-error: var(--destructive);
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.sense-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  border-bottom: 1px solid var(--sense-border);
  padding: 0.7rem 0.85rem;
}

.sense-header-state {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  border: 1px solid var(--sense-border);
  border-radius: var(--radius-md);
  background: var(--sense-panel-bg-soft);
  padding: 0.35rem 0.55rem;
  color: var(--sense-muted);
  font-size: 0.68rem;
  font-weight: 800;
  letter-spacing: 0.04em;
}

.sense-stage {
  position: relative;
  min-height: 0;
  flex: 1;
  overflow: hidden;
}

.sense-grid {
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(var(--sense-border) 1px, transparent 1px),
    linear-gradient(90deg, var(--sense-border) 1px, transparent 1px);
  background-size: 40px 40px;
}

.sense-canvas {
  position: relative;
  z-index: 1;
  width: 100%;
  height: 100%;
  min-height: 24rem;
}

.sense-lanes line,
.core-rings circle {
  fill: none;
  stroke: var(--sense-border);
  stroke-width: 1;
  stroke-dasharray: 5 9;
}

.core-rings circle {
  transform-origin: 400px 210px;
  opacity: 0.7;
}

.attack-beam,
.defense-wave,
.service-alert-line,
.beam-fragments path {
  fill: none;
  stroke-linecap: round;
  stroke-width: 4;
}

.attack-beam {
  stroke: var(--sense-attack);
  stroke-dasharray: 34 420;
  animation: beam-run 1500ms cubic-bezier(0.16, 1, 0.3, 1) infinite;
}

.attack-beam-out {
  animation-delay: 180ms;
}

.beam-success {
  stroke-width: 4.5;
}

.beam-fragments path {
  stroke: var(--sense-failed);
  stroke-dasharray: 20 18;
  animation: beam-break 1200ms ease-out infinite;
}

.defense-wave {
  stroke: var(--sense-defense);
  stroke-dasharray: 22 14;
  animation: defense-wave 1700ms ease-out infinite;
}

.wave-complete {
  stroke-width: 4.5;
}

.wave-broken {
  stroke: var(--sense-failed);
  stroke-dasharray: 16 18;
}

.service-alert-line {
  stroke: var(--sense-error);
  stroke-dasharray: 14 14;
  animation: service-flicker 900ms steps(2, end) infinite;
}

.service-alert-line-right {
  animation-delay: 160ms;
}

.sense-node circle:first-child {
  fill: var(--sense-node);
  stroke: var(--sense-border-strong);
  stroke-width: 1.5;
}

.sense-node .node-ring {
  fill: none;
  stroke: var(--sense-border);
  stroke-width: 1;
}

.sense-node.active circle:first-child {
  fill: var(--sense-attack);
  stroke: var(--sense-text);
}

.sense-node.active .node-ring {
  stroke: var(--sense-attack);
  animation: node-pulse 1200ms ease-out infinite;
}

.sense-defense-success .sense-node.active circle:first-child,
.sense-defense-failed .sense-node.active circle:first-child {
  fill: var(--sense-defense);
}

.sense-defense-success .sense-node.active .node-ring,
.sense-defense-failed .sense-node.active .node-ring {
  stroke: var(--sense-defense);
}

.sense-service-error .sense-node.active circle:first-child {
  fill: var(--sense-error);
}

.sense-service-error .sense-node.active .node-ring {
  stroke: var(--sense-error);
  animation: service-flicker 850ms steps(2, end) infinite;
}

.node-label {
  fill: var(--sense-text);
  font-size: 12px;
  font-weight: 700;
}

.node-sub {
  fill: var(--sense-muted-soft);
  font-size: 10px;
  font-weight: 700;
}

.impact-zone circle {
  fill: none;
  opacity: 0;
  stroke-width: 2;
}

.impact-zone.active circle {
  animation: impact-ring 1400ms ease-out infinite;
}

.sense-attack-success .impact-zone circle {
  stroke: var(--sense-attack);
}

.sense-attack-failed .impact-zone circle,
.sense-defense-failed .impact-zone circle {
  stroke: var(--sense-failed);
}

.sense-defense-success .impact-zone circle {
  stroke: var(--sense-defense);
}

.sense-service-error .impact-zone circle {
  stroke: var(--sense-error);
}

.sense-core {
  position: absolute;
  z-index: 2;
  top: 50%;
  left: 50%;
  width: 14.5rem;
  transform: translate(-50%, -50%);
}

.core-orbit {
  position: absolute;
  inset: -1.15rem;
  border: 1px solid var(--sense-border);
  border-radius: 50%;
}

.sense-defense-success .core-orbit,
.sense-defense-failed .core-orbit {
  border-color: var(--sense-defense);
}

.sense-service-error .core-orbit {
  border-color: var(--sense-error);
}

.core-card {
  position: relative;
  border: 1px solid var(--sense-attack);
  border-radius: var(--radius-md);
  background: var(--sense-panel-bg);
  padding: 1rem;
  text-align: center;
  box-shadow: none;
}

.sense-defense-success .core-card,
.sense-defense-failed .core-card {
  border-color: var(--sense-defense);
  box-shadow: none;
}

.sense-service-error .core-card {
  border-color: var(--sense-error);
  box-shadow: none;
}

.core-round {
  color: var(--sense-muted-soft);
  font-size: 0.7rem;
  font-weight: 800;
  letter-spacing: 0.08em;
}

.core-result {
  margin-top: 0.45rem;
  color: var(--sense-text);
  font-size: 1.35rem;
  font-weight: 900;
  letter-spacing: 0.02em;
}

.sense-defense-success .core-result {
  color: var(--sense-defense);
}

.sense-defense-failed .core-result,
.sense-attack-failed .core-result {
  color: var(--sense-failed);
}

.sense-service-error .core-result {
  color: var(--sense-error);
}

.core-route {
  margin-top: 0.55rem;
  overflow: hidden;
  color: var(--sense-muted);
  font-size: 0.8rem;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.sense-empty {
  position: absolute;
  z-index: 3;
  right: 1rem;
  bottom: 1rem;
  max-width: 17rem;
  border: 1px solid var(--sense-border);
  border-radius: var(--radius-md);
  background: var(--sense-panel-bg-soft);
  padding: 0.75rem;
  color: var(--sense-muted-soft);
  font-size: 0.75rem;
}

.sense-footer {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  border-top: 1px solid var(--sense-border);
  padding: 0.55rem 0.85rem;
  color: var(--sense-muted-soft);
  font-size: 0.68rem;
  font-weight: 800;
}

@keyframes beam-run {
  0% {
    stroke-dashoffset: 420;
    opacity: 0;
  }
  18% {
    opacity: 1;
  }
  100% {
    stroke-dashoffset: 0;
    opacity: 0.85;
  }
}

@keyframes beam-break {
  0%, 100% {
    opacity: 0.42;
    stroke-dashoffset: 0;
  }
  45% {
    opacity: 1;
    stroke-dashoffset: -28;
  }
}

@keyframes defense-wave {
  0% {
    opacity: 0;
    stroke-dashoffset: 70;
  }
  40% {
    opacity: 1;
  }
  100% {
    opacity: 0.78;
    stroke-dashoffset: 0;
  }
}

@keyframes service-flicker {
  0%, 100% {
    opacity: 0.45;
  }
  50% {
    opacity: 1;
  }
}

@keyframes node-pulse {
  0% {
    opacity: 0.9;
    transform: scale(0.92);
  }
  100% {
    opacity: 0;
    transform: scale(1.45);
  }
}

@keyframes impact-ring {
  0% {
    opacity: 0.9;
    transform: scale(0.7);
    transform-origin: 706px 210px;
  }
  100% {
    opacity: 0;
    transform: scale(1.25);
    transform-origin: 706px 210px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .core-rings circle,
  .attack-beam,
  .defense-wave,
  .service-alert-line,
  .beam-fragments path,
  .sense-node.active .node-ring,
  .impact-zone.active circle,
  .core-orbit {
    animation: none;
  }
}
</style>
