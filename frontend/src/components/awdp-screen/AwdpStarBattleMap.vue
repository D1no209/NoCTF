<script setup lang="ts">
import type { AwdpChallengeStatus, AwdpScreenEvent, AwdpTeamScore } from '@/types/awdpScreen'
import { computed } from 'vue'

interface Props {
  teams: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  events: AwdpScreenEvent[]
  currentRound?: number
  maxBeams?: number
  maxTeams?: number
  maxChallenges?: number
  activeEventId?: string
}

const props = withDefaults(defineProps<Props>(), {
  currentRound: 0,
  maxBeams: 10,
  maxTeams: 12,
  maxChallenges: 6,
})

const VIEW_W = 800
const VIEW_H = 480
const CENTER_X = VIEW_W / 2
const CENTER_Y = VIEW_H / 2
const SERVICE_RADIUS = 130
const TEAM_RADIUS = 240

const categoryColors: Record<string, string> = {
  web: '#0891b2',
  pwn: '#db2777',
  crypto: '#7c3aed',
  reverse: '#059669',
  misc: '#ca8a04',
}

const NODE_FILL = '#f8fafc'
const NODE_STROKE = '#64748b'

function toRad(deg: number) {
  return (deg * Math.PI) / 180
}

function serviceColor(category: string) {
  return categoryColors[category.toLowerCase()] ?? '#64748b'
}

// deterministic rng
function mulberry32(seed: number) {
  return function () {
    let t = seed += 0x6D2B79F5
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const stars = computed(() => {
  const rng = mulberry32(0x5f3759df)
  return Array.from({ length: 60 }, () => {
    const x = rng() * VIEW_W
    const y = rng() * VIEW_H
    const r = 0.5 + rng() * 1.2
    const opacity = 0.12 + rng() * 0.35
    const dx = (rng() - 0.5) * 6
    const dy = (rng() - 0.5) * 6
    const duration = 6 + rng() * 8
    const delay = rng() * -10
    return { x, y, r, opacity, dx, dy, duration, delay }
  })
})

const dust = computed(() => {
  const rng = mulberry32(0x6d2b79f5)
  return Array.from({ length: 28 }, () => {
    const x = rng() * VIEW_W
    const y = rng() * VIEW_H
    const r = 1 + rng() * 2.5
    const opacity = 0.08 + rng() * 0.2
    const dx = (rng() - 0.5) * 22
    const dy = (rng() - 0.5) * 22
    const duration = 10 + rng() * 14
    const delay = rng() * -12
    return { x, y, r, opacity, dx, dy, duration, delay }
  })
})

const displayChallenges = computed(() => props.challenges.slice(0, props.maxChallenges))
const displayTeams = computed(() => [...props.teams].sort((a, b) => a.rank - b.rank).slice(0, props.maxTeams))

const serviceNodes = computed(() => {
  const count = displayChallenges.value.length
  return displayChallenges.value.map((challenge, index) => {
    const angle = toRad(-90 + (360 / Math.max(1, count)) * index)
    return {
      ...challenge,
      x: CENTER_X + Math.cos(angle) * SERVICE_RADIUS,
      y: CENTER_Y + Math.sin(angle) * SERVICE_RADIUS,
    }
  })
})

const teamNodes = computed(() => {
  const count = displayTeams.value.length
  return displayTeams.value.map((team, index) => {
    const angle = toRad(-150 + (300 / Math.max(1, count - 1)) * index)
    return {
      ...team,
      x: CENTER_X + Math.cos(angle) * TEAM_RADIUS,
      y: CENTER_Y + Math.sin(angle) * TEAM_RADIUS,
    }
  })
})

const serviceMesh = computed(() => {
  const nodes = serviceNodes.value
  const lines: Array<{ x1: number, y1: number, x2: number, y2: number }> = []
  for (let i = 0; i < nodes.length; i++) {
    const a = nodes[i]
    const b = nodes[(i + 1) % nodes.length]
    lines.push({ x1: a.x, y1: a.y, x2: b.x, y2: b.y })
    lines.push({ x1: a.x, y1: a.y, x2: CENTER_X, y2: CENTER_Y })
  }
  return lines
})

const operationalEvents = computed(() => {
  return props.events.filter(event =>
    event.type !== 'SCORE_UPDATED'
    && event.type !== 'TEAM_RANK_CHANGED'
    && event.type !== 'ROUND_STARTED'
    && event.type !== 'ROUND_ENDED',
  )
})

const currentEvent = computed(() => {
  if (props.activeEventId)
    return operationalEvents.value.find(event => event.id === props.activeEventId) ?? operationalEvents.value[0] ?? null
  return operationalEvents.value[0] ?? null
})

const beams = computed(() => {
  return operationalEvents.value.slice(0, props.maxBeams).map((event) => {
    const team = teamNodes.value.find(t => t.teamId === event.teamId || t.teamName === event.teamName)
    const service = serviceNodes.value.find(s => s.challengeId === event.challengeId || s.challengeName === event.challengeName)
    if (!team || !service)
      return null

    const isActive = event.id === currentEvent.value?.id
    const mx = (team.x + service.x) / 2
    const my = (team.y + service.y) / 2
    const controlX = CENTER_X + (mx - CENTER_X) * 0.3
    const controlY = CENTER_Y + (my - CENTER_Y) * 0.3
    const d = `M ${team.x.toFixed(1)} ${team.y.toFixed(1)} Q ${controlX.toFixed(1)} ${controlY.toFixed(1)} ${service.x.toFixed(1)} ${service.y.toFixed(1)}`

    return {
      id: event.id,
      d,
      color: beamColor(event.type),
      isActive,
      label: beamLabel(event.type),
      dotDuration: isActive ? 900 : 1700,
    }
  }).filter((item): item is NonNullable<typeof item> => Boolean(item))
})

const activeTeamIds = computed(() => {
  const ids = new Set<string>()
  if (currentEvent.value?.teamId)
    ids.add(currentEvent.value.teamId)
  return ids
})

const activeChallengeIds = computed(() => {
  const ids = new Set<string>()
  if (currentEvent.value?.challengeId)
    ids.add(currentEvent.value.challengeId)
  return ids
})

function beamColor(type: AwdpScreenEvent['type']) {
  if (type === 'ATTACK_ACCEPTED')
    return '#0891b2'
  if (type === 'ATTACK_REJECTED')
    return '#d97706'
  if (type === 'DEFENSE_CHECK_PASSED' || type === 'PATCH_UPLOADED')
    return '#059669'
  if (type === 'DEFENSE_CHECK_FAILED')
    return '#d97706'
  if (type === 'SERVICE_ERROR')
    return '#e11d48'
  if (type === 'ATTACK_SUBMITTED')
    return '#64748b'
  return '#64748b'
}

function beamLabel(type: AwdpScreenEvent['type']) {
  if (type === 'ATTACK_ACCEPTED')
    return '命中'
  if (type === 'ATTACK_REJECTED')
    return '被防'
  if (type === 'DEFENSE_CHECK_PASSED')
    return '防御成功'
  if (type === 'DEFENSE_CHECK_FAILED')
    return '防御失败'
  if (type === 'SERVICE_ERROR')
    return '服务异常'
  if (type === 'PATCH_UPLOADED')
    return 'Patch'
  return type
}

function textAnchor(x: number) {
  return x < CENTER_X - 20 ? 'end' : x > CENTER_X + 20 ? 'start' : 'middle'
}

function teamLabelX(x: number) {
  const offset = x < CENTER_X ? -16 : 16
  return x + offset
}

function serviceLabelX(x: number) {
  const offset = x < CENTER_X ? -20 : x > CENTER_X ? 20 : 0
  return x + offset
}

function serviceLabelY(y: number) {
  return y + (y < CENTER_Y ? -18 : 22)
}
</script>

<template>
  <svg
    :viewBox="`0 0 ${VIEW_W} ${VIEW_H}`"
    class="block h-full w-full"
    preserveAspectRatio="xMidYMid meet"
    aria-hidden="true"
  >
    <defs>
      <radialGradient id="star-battle-nebula" cx="50%" cy="50%" r="50%">
        <stop offset="0%" stop-color="#cbd5e1" stop-opacity="0.28" />
        <stop offset="60%" stop-color="#e2e8f0" stop-opacity="0.08" />
        <stop offset="100%" stop-color="#f8fafc" stop-opacity="0" />
      </radialGradient>
    </defs>

    <!-- soft nebula core -->
    <circle
      :cx="CENTER_X"
      :cy="CENTER_Y"
      r="210"
      fill="url(#star-battle-nebula)"
    />

    <!-- drifting stars -->
    <g class="star-battle-stars">
      <circle
        v-for="(star, index) in stars"
        :key="`s-${index}`"
        :cx="star.x"
        :cy="star.y"
        :r="star.r"
        class="star-battle-star fill-slate-500"
        :opacity="star.opacity"
        :style="{
          '--dx': `${star.dx}px`,
          '--dy': `${star.dy}px`,
          '--duration': `${star.duration}s`,
          '--delay': `${star.delay}s`,
        }"
      />
    </g>

    <!-- drifting dust -->
    <g class="star-battle-dust-layer">
      <circle
        v-for="(p, index) in dust"
        :key="`d-${index}`"
        :cx="p.x"
        :cy="p.y"
        :r="p.r"
        class="star-battle-dust fill-slate-400"
        :opacity="p.opacity"
        :style="{
          '--dx': `${p.dx}px`,
          '--dy': `${p.dy}px`,
          '--duration': `${p.duration}s`,
          '--delay': `${p.delay}s`,
        }"
      />
    </g>

    <!-- constellation mesh -->
    <g class="star-battle-mesh">
      <line
        v-for="(seg, index) in serviceMesh"
        :key="`m-${index}`"
        :x1="seg.x1"
        :y1="seg.y1"
        :x2="seg.x2"
        :y2="seg.y2"
        stroke="currentColor"
        class="text-slate-400/15"
        stroke-width="1"
        stroke-dasharray="2 8"
      />
    </g>

    <!-- data streams -->
    <g class="star-battle-streams">
      <g
        v-for="beam in beams"
        :key="beam.id"
        :style="{ color: beam.color }"
      >
        <!-- faint tail -->
        <path
          :d="beam.d"
          fill="none"
          :stroke="beam.color"
          stroke-width="6"
          opacity="0.12"
          stroke-linecap="round"
        />

        <!-- flowing data -->
        <path
          :d="beam.d"
          fill="none"
          :stroke="beam.color"
          stroke-width="2.5"
          stroke-dasharray="3 5"
          stroke-linecap="round"
          opacity="0.85"
          :class="beam.isActive ? 'star-battle-stream-active' : 'star-battle-stream-idle'"
        />

        <!-- packet -->
        <circle
          r="3"
          :fill="beam.color"
          opacity="0.95"
          class="star-battle-packet"
        >
          <animateMotion
            :path="beam.d"
            :dur="`${beam.dotDuration}ms`"
            repeatCount="indefinite"
          />
        </circle>
      </g>
    </g>

    <!-- service nodes -->
    <g
      v-for="node in serviceNodes"
      :key="node.challengeId"
      :class="activeChallengeIds.has(node.challengeId) ? 'star-battle-service-active' : ''"
    >
      <!-- halo -->
      <circle
        :cx="node.x"
        :cy="node.y"
        r="26"
        fill="none"
        :stroke="serviceColor(node.category)"
        stroke-width="1"
        opacity="0.14"
        class="star-battle-halo"
      />

      <!-- hit ripple -->
      <circle
        v-if="activeChallengeIds.has(node.challengeId)"
        :cx="node.x"
        :cy="node.y"
        r="24"
        fill="none"
        :stroke="serviceColor(node.category)"
        stroke-width="1"
        class="star-battle-ripple"
        :style="{ transformOrigin: `${node.x}px ${node.y}px` }"
      />

      <!-- core -->
      <circle
        :cx="node.x"
        :cy="node.y"
        r="15"
        :fill="NODE_FILL"
        :stroke="serviceColor(node.category)"
        stroke-width="2.5"
      />

      <!-- active pulse ring -->
      <circle
        :cx="node.x"
        :cy="node.y"
        r="20"
        fill="none"
        :stroke="serviceColor(node.category)"
        stroke-width="1"
        opacity="0"
        :class="activeChallengeIds.has(node.challengeId) ? 'star-battle-pulse' : ''"
      />

      <text
        :x="serviceLabelX(node.x)"
        :y="serviceLabelY(node.y)"
        :text-anchor="textAnchor(node.x)"
        class="fill-slate-800 text-[11px] font-bold"
      >
        {{ node.challengeName }}
      </text>
      <text
        :x="serviceLabelX(node.x)"
        :y="serviceLabelY(node.y) + 12"
        :text-anchor="textAnchor(node.x)"
        class="fill-slate-500 text-[9px] font-semibold uppercase"
      >
        {{ node.category }}
      </text>
    </g>

    <!-- team nodes -->
    <g
      v-for="node in teamNodes"
      :key="node.teamId"
      :class="activeTeamIds.has(node.teamId) ? 'star-battle-team-active' : ''"
    >
      <circle
        :cx="node.x"
        :cy="node.y"
        r="8"
        :fill="NODE_FILL"
        :stroke="NODE_STROKE"
        stroke-width="1.5"
      />
      <circle
        :cx="node.x"
        :cy="node.y"
        r="13"
        fill="none"
        stroke="#0891b2"
        stroke-width="1"
        opacity="0"
        :class="activeTeamIds.has(node.teamId) ? 'star-battle-pulse' : ''"
      />
      <text
        :x="teamLabelX(node.x)"
        :y="node.y + 4"
        :text-anchor="textAnchor(node.x)"
        class="fill-slate-700 text-[10px] font-semibold"
      >
        #{{ node.rank }} {{ node.teamName }}
      </text>
    </g>
  </svg>
</template>

<style scoped>
.star-battle-star {
  animation: star-battle-drift var(--duration, 8s) ease-in-out infinite alternate;
  animation-delay: var(--delay, 0s);
}

.star-battle-dust {
  animation: star-battle-drift var(--duration, 12s) ease-in-out infinite alternate;
  animation-delay: var(--delay, 0s);
}

.star-battle-mesh {
  animation: star-battle-mesh-flow 28s linear infinite;
}

.star-battle-stream-active {
  animation: star-battle-data-flow 700ms linear infinite;
  filter: drop-shadow(0 0 7px currentColor);
}

.star-battle-stream-idle {
  animation: star-battle-data-flow 1600ms linear infinite;
  opacity: 0.55;
}

.star-battle-packet {
  filter: drop-shadow(0 0 5px currentColor);
}

.star-battle-pulse {
  animation: star-battle-node-pulse 1200ms ease-out infinite;
}

.star-battle-halo {
  animation: star-battle-halo-breathe 4s ease-in-out infinite;
}

.star-battle-service-active .star-battle-halo {
  animation: star-battle-halo-breathe 1.2s ease-in-out infinite;
  opacity: 0.35;
}

.star-battle-ripple {
  animation: star-battle-ripple-expand 2.2s ease-out infinite;
}

.star-battle-team-active circle:first-of-type {
  filter: drop-shadow(0 0 6px rgba(8, 145, 178, 0.6));
}

@keyframes star-battle-drift {
  0% {
    transform: translate(0, 0);
  }
  100% {
    transform: translate(var(--dx, 0px), var(--dy, 0px));
  }
}

@keyframes star-battle-mesh-flow {
  from {
    stroke-dashoffset: 120;
  }
  to {
    stroke-dashoffset: 0;
  }
}

@keyframes star-battle-data-flow {
  from {
    stroke-dashoffset: 64;
  }
  to {
    stroke-dashoffset: 0;
  }
}

@keyframes star-battle-node-pulse {
  0% {
    opacity: 0.55;
    transform: scale(0.92);
  }
  100% {
    opacity: 0;
    transform: scale(1.5);
  }
}

@keyframes star-battle-halo-breathe {
  0%, 100% {
    opacity: 0.08;
    transform: scale(1);
  }
  50% {
    opacity: 0.22;
    transform: scale(1.08);
  }
}

@keyframes star-battle-ripple-expand {
  0% {
    opacity: 0.45;
    transform: scale(0.6);
  }
  100% {
    opacity: 0;
    transform: scale(2.4);
  }
}

@media (prefers-reduced-motion: reduce) {
  .star-battle-star,
  .star-battle-dust,
  .star-battle-mesh,
  .star-battle-stream-active,
  .star-battle-stream-idle,
  .star-battle-pulse,
  .star-battle-halo,
  .star-battle-service-active .star-battle-halo,
  .star-battle-ripple {
    animation: none;
  }
}
</style>
