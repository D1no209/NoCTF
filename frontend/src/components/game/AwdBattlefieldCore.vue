<script setup lang="ts">
import type { AttackLogDto } from '@/components/game/AttackLogFeed.vue'
import type { ServiceStatus } from '@/components/game/ServiceStatusGrid.vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

export type AwdAwarenessEventType
  = | 'attack'
    | 'service_down'
    | 'service_recovered'
    | 'round_start'
    | 'flag_refresh'
    | 'checker_error'
    | 'gamebox_restart'

export type AwdAwarenessEventResult
  = | 'success'
    | 'failed'
    | 'error'
    | 'recovered'
    | 'system'

export interface AwdAwarenessEvent {
  id?: string
  type: AwdAwarenessEventType
  result?: AwdAwarenessEventResult
  attackerTeamId?: string
  attackerTeamName?: string
  victimTeamId?: string
  victimTeamName?: string
  teamId?: string
  teamName?: string
  challengeId?: string
  challengeName?: string
  serviceName?: string
  round?: number
  timestamp?: string
  message?: string
  reason?: string
}

interface TeamNode {
  id: string
  name: string
  healthy: number
  down: number
  unknown: number
  total: number
}

interface ServiceNode {
  id: string
  name: string
  healthy: number
  down: number
  unknown: number
  total: number
}

const props = withDefaults(defineProps<{
  round: number
  services: ServiceStatus[]
  attackLogs: AttackLogDto[]
  externalEvents?: AwdAwarenessEvent[]
}>(), {
  externalEvents: () => [],
})

const eventQueue = ref<AwdAwarenessEvent[]>([])
const activeEvent = ref<AwdAwarenessEvent | null>(null)
const playedEvents = ref<AwdAwarenessEvent[]>([])
const isPlaying = ref(false)
const playbackKey = ref(0)
const knownEventKeys = new Set<string>()
const serviceState = new Map<string, ServiceStatus['status']>()
const timers = new Set<ReturnType<typeof window.setTimeout>>()
let initializedServices = false
let initializedRound = false
let lastRound = props.round

const teamNodes = computed<TeamNode[]>(() => {
  const map = new Map<string, TeamNode>()

  for (const service of props.services) {
    const node = map.get(service.teamId) ?? {
      id: service.teamId,
      name: service.teamName,
      healthy: 0,
      down: 0,
      unknown: 0,
      total: 0,
    }

    node.total += 1
    if (service.status === 'healthy')
      node.healthy += 1
    else if (service.status === 'down')
      node.down += 1
    else
      node.unknown += 1

    map.set(service.teamId, node)
  }

  return Array.from(map.values()).sort((first, second) => first.name.localeCompare(second.name))
})

const serviceNodes = computed<ServiceNode[]>(() => {
  const map = new Map<string, ServiceNode>()

  for (const service of props.services) {
    const node = map.get(service.challengeId) ?? {
      id: service.challengeId,
      name: service.challengeName,
      healthy: 0,
      down: 0,
      unknown: 0,
      total: 0,
    }

    node.total += 1
    if (service.status === 'healthy')
      node.healthy += 1
    else if (service.status === 'down')
      node.down += 1
    else
      node.unknown += 1

    map.set(service.challengeId, node)
  }

  return Array.from(map.values()).sort((first, second) => first.name.localeCompare(second.name))
})

const healthyRate = computed(() => {
  const total = props.services.length
  if (total === 0)
    return 0

  const healthy = props.services.filter(service => service.status === 'healthy').length
  return Math.round((healthy / total) * 100)
})

const activeTone = computed(() => {
  const event = activeEvent.value
  if (!event)
    return 'idle'
  if (event.type === 'attack')
    return event.result === 'success' ? 'attack-success' : 'attack-failed'
  if (event.type === 'service_down')
    return 'service-down'
  if (event.type === 'service_recovered')
    return 'service-recovered'
  if (event.type === 'checker_error')
    return 'checker-error'
  if (event.type === 'gamebox_restart')
    return 'gamebox-restart'
  return 'system'
})

const coreMode = computed(() => {
  const event = activeEvent.value
  if (!event)
    return 'MONITORING'
  if (event.type === 'attack')
    return 'ATTACK'
  if (event.type === 'service_down')
    return 'SERVICE'
  if (event.type === 'service_recovered')
    return 'RECOVERY'
  if (event.type === 'checker_error')
    return 'DIAGNOSTIC'
  return 'SYSTEM'
})

const coreResult = computed(() => {
  const event = activeEvent.value
  if (!event)
    return 'NO ACTIVE INCIDENT'

  return eventTitle(event)
})

const coreSummary = computed(() => {
  const event = activeEvent.value
  if (!event)
    return 'AWD battlefield telemetry listening'

  return eventSummary(event)
})

const displayedRound = computed(() => activeEvent.value?.round ?? props.round)
const pendingCount = computed(() => eventQueue.value.length)
const isIdle = computed(() => !activeEvent.value && eventQueue.value.length === 0)

watch(
  () => props.attackLogs,
  (logs) => {
    const chronological = [...logs].sort((first, second) =>
      Date.parse(first.timestamp) - Date.parse(second.timestamp),
    )

    for (const log of chronological)
      enqueueEvent(attackLogToEvent(log))
  },
  { deep: true, immediate: true },
)

watch(
  () => props.externalEvents,
  (events) => {
    for (const event of events)
      enqueueEvent({ ...event, timestamp: event.timestamp ?? new Date().toISOString() })
  },
  { deep: true, immediate: true },
)

watch(
  () => props.services,
  (services) => {
    if (!initializedServices) {
      for (const service of services)
        serviceState.set(serviceKey(service), service.status)
      initializedServices = true
      return
    }

    for (const service of services) {
      const key = serviceKey(service)
      const previous = serviceState.get(key)
      serviceState.set(key, service.status)

      if (!previous || previous === service.status)
        continue

      if (service.status === 'down') {
        enqueueEvent({
          type: 'service_down',
          result: 'error',
          teamId: service.teamId,
          teamName: service.teamName,
          challengeId: service.challengeId,
          challengeName: service.challengeName,
          round: props.round,
          timestamp: new Date().toISOString(),
          reason: 'Checker reported service down',
        })
      }
      else if (service.status === 'healthy' && previous === 'down') {
        enqueueEvent({
          type: 'service_recovered',
          result: 'recovered',
          teamId: service.teamId,
          teamName: service.teamName,
          challengeId: service.challengeId,
          challengeName: service.challengeName,
          round: props.round,
          timestamp: new Date().toISOString(),
        })
      }
      else if (service.status === 'unknown') {
        enqueueEvent({
          type: 'checker_error',
          result: 'error',
          teamId: service.teamId,
          teamName: service.teamName,
          challengeId: service.challengeId,
          challengeName: service.challengeName,
          round: props.round,
          timestamp: new Date().toISOString(),
          reason: 'Checker status unknown',
        })
      }
    }
  },
  { deep: true, immediate: true },
)

watch(
  () => props.round,
  (round) => {
    if (!initializedRound) {
      initializedRound = true
      lastRound = round
      return
    }

    if (round > 0 && round !== lastRound) {
      enqueueEvent({
        type: 'round_start',
        result: 'system',
        round,
        timestamp: new Date().toISOString(),
      })
      enqueueEvent({
        type: 'flag_refresh',
        result: 'system',
        round,
        timestamp: new Date().toISOString(),
      })
    }

    lastRound = round
  },
  { immediate: true },
)

watch(
  () => eventQueue.value.length,
  () => {
    if (!isPlaying.value)
      void playQueue()
  },
)

onBeforeUnmount(() => {
  for (const timer of timers)
    window.clearTimeout(timer)
  timers.clear()
})

function attackLogToEvent(log: AttackLogDto): AwdAwarenessEvent {
  return {
    id: `attack:${log.attackerTeamId}:${log.victimTeamId}:${log.challengeId}:${log.roundNumber}:${log.timestamp}`,
    type: 'attack',
    result: 'success',
    attackerTeamId: log.attackerTeamId,
    attackerTeamName: log.attackerTeamName,
    victimTeamId: log.victimTeamId,
    victimTeamName: log.victimTeamName,
    challengeId: log.challengeId,
    challengeName: log.challengeName,
    round: log.roundNumber,
    timestamp: log.timestamp,
  }
}

function serviceKey(service: ServiceStatus) {
  return `${service.teamId}:${service.challengeId}`
}

function enqueueEvent(event: AwdAwarenessEvent) {
  const eventWithId = {
    ...event,
    id: event.id ?? eventKey(event),
    timestamp: event.timestamp ?? new Date().toISOString(),
  }

  if (knownEventKeys.has(eventWithId.id))
    return

  knownEventKeys.add(eventWithId.id)
  eventQueue.value.push(eventWithId)
}

async function playQueue() {
  if (isPlaying.value)
    return

  isPlaying.value = true

  while (eventQueue.value.length > 0) {
    const nextEvent = eventQueue.value.shift()
    if (!nextEvent)
      continue

    activeEvent.value = nextEvent
    playbackKey.value += 1

    await delay(eventDuration(nextEvent))
    appendPlayedEvent(nextEvent)
    activeEvent.value = null
    await delay(420)
  }

  isPlaying.value = false

  if (eventQueue.value.length > 0)
    void playQueue()
}

function delay(ms: number) {
  return new Promise<void>((resolve) => {
    const timer = window.setTimeout(() => {
      timers.delete(timer)
      resolve()
    }, ms)
    timers.add(timer)
  })
}

function appendPlayedEvent(event: AwdAwarenessEvent) {
  playedEvents.value.unshift(event)
  if (playedEvents.value.length > 5)
    playedEvents.value.splice(5)
}

function eventDuration(event: AwdAwarenessEvent) {
  if (event.type === 'attack')
    return event.result === 'success' ? 1900 : 2100
  if (event.type === 'service_down')
    return 2200
  if (event.type === 'service_recovered')
    return 1900
  if (event.type === 'round_start' || event.type === 'flag_refresh')
    return 1400
  return 1800
}

function eventKey(event: AwdAwarenessEvent) {
  return [
    event.type,
    event.result,
    event.attackerTeamId,
    event.victimTeamId,
    event.teamId,
    event.challengeId,
    event.round,
    event.timestamp,
    event.message,
  ].filter(Boolean).join(':')
}

function eventTitle(event: AwdAwarenessEvent) {
  if (event.type === 'attack')
    return event.result === 'success' ? 'ATTACK SUCCESS' : 'ATTACK FAILED'
  if (event.type === 'service_down')
    return 'SERVICE DOWN'
  if (event.type === 'service_recovered')
    return 'SERVICE RECOVERED'
  if (event.type === 'round_start')
    return `ROUND ${event.round ?? displayedRound.value} START`
  if (event.type === 'flag_refresh')
    return 'FLAG REFRESHED'
  if (event.type === 'checker_error')
    return 'CHECKER ERROR'
  return 'GAMEBOX RESTARTING'
}

function eventSummary(event: AwdAwarenessEvent) {
  const challengeName = event.challengeName ?? event.serviceName ?? 'service'

  if (event.type === 'attack') {
    return `${event.attackerTeamName ?? 'Attacker'} -> ${event.victimTeamName ?? 'Target'} / ${challengeName}`
  }

  if (event.type === 'service_down' || event.type === 'service_recovered' || event.type === 'gamebox_restart')
    return `${event.teamName ?? 'Team'} / ${challengeName}`

  if (event.type === 'checker_error')
    return `${challengeName}${event.reason ? ` / ${event.reason}` : ''}`

  return event.type === 'flag_refresh'
    ? `Round ${event.round ?? displayedRound.value} flag rotation`
    : `Round ${event.round ?? displayedRound.value}`
}

function eventLogLine(event: AwdAwarenessEvent) {
  const time = formatTime(event.timestamp)
  const challengeName = event.challengeName ?? event.serviceName ?? 'service'

  if (event.type === 'attack') {
    const result = event.result === 'success' ? 'success' : 'failed'
    return `${time} ${event.attackerTeamName ?? 'Attacker'} attack ${event.victimTeamName ?? 'Target'} / ${challengeName} ${result}`
  }

  if (event.type === 'service_down')
    return `${time} ${event.teamName ?? 'Team'} / ${challengeName} service down`

  if (event.type === 'service_recovered')
    return `${time} ${event.teamName ?? 'Team'} / ${challengeName} service recovered`

  if (event.type === 'round_start')
    return `${time} round ${event.round ?? displayedRound.value} start`

  if (event.type === 'flag_refresh')
    return `${time} round ${event.round ?? displayedRound.value} flag refreshed`

  if (event.type === 'checker_error')
    return `${time} ${challengeName} checker error`

  return `${time} ${event.teamName ?? 'Team'} / ${challengeName} gamebox restarting`
}

function formatTime(value?: string) {
  if (!value)
    return '--:--:--'

  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value

  return date.toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function isActiveTeam(node: TeamNode) {
  const event = activeEvent.value
  if (!event)
    return false

  return event.attackerTeamId === node.id
    || event.victimTeamId === node.id
    || event.teamId === node.id
    || event.attackerTeamName === node.name
    || event.victimTeamName === node.name
    || event.teamName === node.name
}

function teamRole(node: TeamNode) {
  const event = activeEvent.value
  if (!event)
    return ''
  if (event.attackerTeamId === node.id || event.attackerTeamName === node.name)
    return 'attacker'
  if (event.victimTeamId === node.id || event.victimTeamName === node.name)
    return 'victim'
  if (event.teamId === node.id || event.teamName === node.name)
    return 'subject'
  return ''
}

function isActiveService(node: ServiceNode) {
  const event = activeEvent.value
  if (!event)
    return false

  return event.challengeId === node.id
    || event.challengeName === node.name
    || event.serviceName === node.name
}

function teamHealthLabel(node: TeamNode) {
  if (node.down > 0)
    return `${node.down} down`
  if (node.unknown > 0)
    return `${node.unknown} unknown`
  return 'all up'
}

function serviceHealthLabel(node: ServiceNode) {
  if (node.down > 0)
    return `${node.down}/${node.total} down`
  if (node.unknown > 0)
    return `${node.unknown}/${node.total} unknown`
  return `${node.healthy}/${node.total} up`
}
</script>

<template>
  <section
    class="awd-battlefield-core"
    :class="[`is-${activeTone}`, { 'is-idle': isIdle }]"
    aria-live="polite"
  >
    <div class="battlefield-heading">
      <div>
        <p class="battlefield-kicker">
          AWD realtime awareness
        </p>
        <h2>Central attack field</h2>
      </div>
      <div class="battlefield-metrics">
        <span>Round <strong>{{ displayedRound || 0 }}</strong></span>
        <span>Queue <strong>{{ pendingCount }}</strong></span>
        <span>SLA <strong>{{ healthyRate }}%</strong></span>
      </div>
    </div>

    <div class="battlefield-grid">
      <aside class="node-rail team-rail">
        <div class="rail-title">
          <span>Teams</span>
          <strong>{{ teamNodes.length }}</strong>
        </div>
        <div v-if="teamNodes.length === 0" class="empty-rail">
          Waiting for GameBox telemetry
        </div>
        <div v-else class="node-list">
          <div
            v-for="node in teamNodes"
            :key="node.id"
            class="battle-node team-node"
            :class="[
              { 'is-active-node': isActiveTeam(node), 'has-fault': node.down > 0 },
              teamRole(node),
            ]"
          >
            <div class="node-main">
              <span class="node-dot" />
              <span class="node-name">{{ node.name }}</span>
            </div>
            <span class="node-sub">{{ teamHealthLabel(node) }}</span>
          </div>
        </div>
      </aside>

      <div class="battle-stage">
        <div class="stage-grid" />
        <div class="idle-pulse-field">
          <span />
          <span />
          <span />
        </div>

        <div
          v-if="activeEvent"
          :key="playbackKey"
          class="event-path-layer"
          :class="`event-${activeTone}`"
        >
          <span class="beam beam-in" />
          <span class="beam beam-out" />
          <span class="beam-break" />
          <span class="impact-ring" />
          <span class="service-wave" />
          <span class="system-wave" />
          <span class="diagnostic-scan" />
        </div>

        <div class="core-orbit outer-orbit" />
        <div class="core-orbit inner-orbit" />
        <div class="battle-core">
          <div class="core-ring" />
          <div class="core-content">
            <span class="core-round-label">ROUND</span>
            <strong class="core-round-value">{{ displayedRound || 0 }}</strong>
            <span class="core-mode">{{ coreMode }}</span>
            <span class="core-result">{{ coreResult }}</span>
            <span class="core-summary">{{ coreSummary }}</span>
          </div>
        </div>
      </div>

      <aside class="node-rail service-rail">
        <div class="rail-title">
          <span>Services</span>
          <strong>{{ serviceNodes.length }}</strong>
        </div>
        <div v-if="serviceNodes.length === 0" class="empty-rail">
          Waiting for challenge services
        </div>
        <div v-else class="node-list">
          <div
            v-for="node in serviceNodes"
            :key="node.id"
            class="battle-node service-node"
            :class="{ 'is-active-node': isActiveService(node), 'has-fault': node.down > 0 }"
          >
            <div class="node-main">
              <span class="node-dot" />
              <span class="node-name">{{ node.name }}</span>
            </div>
            <span class="node-sub">{{ serviceHealthLabel(node) }}</span>
          </div>
        </div>
      </aside>
    </div>

    <div class="battlefield-footer">
      <div class="state-strip">
        <span :class="`tone-${activeTone}`">{{ isIdle ? 'IDLE MODE' : 'EVENT PLAYBACK' }}</span>
        <span>{{ activeEvent ? eventTitle(activeEvent) : 'Monitoring service checks and flag submissions' }}</span>
      </div>
      <div class="event-log">
        <p v-if="playedEvents.length === 0">
          No played events in this view.
        </p>
        <template v-else>
          <p v-for="event in playedEvents" :key="event.id">
            {{ eventLogLine(event) }}
          </p>
        </template>
      </div>
    </div>
  </section>
</template>

<style scoped>
.awd-battlefield-core {
  position: relative;
  overflow: hidden;
  border: 1px solid oklch(0.38 0.07 252 / 0.58);
  border-radius: 0.75rem;
  background: oklch(0.15 0.035 252);
  color: oklch(0.94 0.018 230);
  box-shadow: 0 18px 54px rgb(7 14 32 / 0.22);
}

.awd-battlefield-core::before {
  position: absolute;
  inset: 0;
  content: "";
  background:
    linear-gradient(rgb(148 163 184 / 0.035) 1px, transparent 1px),
    linear-gradient(90deg, rgb(148 163 184 / 0.035) 1px, transparent 1px);
  background-size: 36px 36px;
  pointer-events: none;
}

.awd-battlefield-core::after {
  position: absolute;
  inset: 0;
  content: "";
  display: none;
  pointer-events: none;
}

.battlefield-heading,
.battlefield-footer,
.battlefield-grid {
  position: relative;
  z-index: 1;
}

.battlefield-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1rem 0.75rem;
}

.battlefield-kicker {
  margin: 0 0 0.2rem;
  color: oklch(0.75 0.08 220);
  font-size: 0.67rem;
  font-weight: 700;
  letter-spacing: 0;
}

.battlefield-heading h2 {
  margin: 0;
  color: oklch(0.97 0.012 230);
  font-size: 1.05rem;
  font-weight: 800;
  letter-spacing: 0;
}

.battlefield-metrics {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.45rem;
  color: oklch(0.75 0.035 230);
  font-size: 0.72rem;
}

.battlefield-metrics span {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  min-height: 1.7rem;
  border: 1px solid rgb(149 196 255 / 0.16);
  border-radius: 999px;
  background: rgb(9 18 38 / 0.52);
  padding: 0 0.65rem;
}

.battlefield-metrics strong {
  color: oklch(0.93 0.08 210);
  font-variant-numeric: tabular-nums;
}

.battlefield-grid {
  display: grid;
  grid-template-columns: minmax(11rem, 0.82fr) minmax(20rem, 1.75fr) minmax(11rem, 0.82fr);
  gap: 0.75rem;
  min-height: 26rem;
  padding: 0 1rem 0.85rem;
}

.node-rail {
  overflow: hidden;
  border: 1px solid rgb(149 196 255 / 0.14);
  border-radius: 0.75rem;
  background: rgb(7 16 34 / 0.54);
  box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.05);
}

.rail-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid rgb(149 196 255 / 0.12);
  padding: 0.75rem 0.8rem 0.6rem;
  color: oklch(0.74 0.04 230);
  font-size: 0.7rem;
  font-weight: 800;
  letter-spacing: 0;
}

.rail-title strong {
  color: oklch(0.91 0.06 212);
  font-variant-numeric: tabular-nums;
}

.node-list {
  display: grid;
  gap: 0.45rem;
  max-height: 23rem;
  overflow-y: auto;
  padding: 0.65rem;
}

.node-list::-webkit-scrollbar {
  width: 0.35rem;
}

.node-list::-webkit-scrollbar-thumb {
  border-radius: 999px;
  background: rgb(149 196 255 / 0.22);
}

.battle-node {
  position: relative;
  display: grid;
  gap: 0.25rem;
  min-height: 3.25rem;
  border: 1px solid rgb(149 196 255 / 0.12);
  border-radius: 0.6rem;
  background: rgb(13 26 54 / 0.48);
  padding: 0.55rem 0.6rem;
  transition:
    border-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1),
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.battle-node.has-fault {
  border-color: rgb(248 113 113 / 0.34);
  background: rgb(67 19 32 / 0.38);
}

.battle-node.is-active-node {
  transform: translateY(-1px);
  opacity: 1;
}

.battle-node.attacker {
  border-color: rgb(251 146 60 / 0.72);
  background: rgb(91 47 18 / 0.6);
  box-shadow: 0 0 24px rgb(251 146 60 / 0.16);
}

.battle-node.victim {
  border-color: rgb(248 113 113 / 0.68);
  background: rgb(77 24 31 / 0.58);
}

.battle-node.subject,
.service-node.is-active-node {
  border-color: rgb(45 212 191 / 0.66);
  background: rgb(14 68 76 / 0.5);
}

.is-service-down .battle-node.subject,
.is-service-down .service-node.is-active-node {
  border-color: rgb(244 63 94 / 0.82);
  background: rgb(88 20 40 / 0.58);
  animation: node-alert 760ms steps(2, end) infinite;
}

.is-checker-error .service-node.is-active-node {
  border-color: rgb(192 132 252 / 0.72);
  background: rgb(48 32 82 / 0.62);
}

.is-service-recovered .battle-node.subject,
.is-service-recovered .service-node.is-active-node {
  border-color: rgb(52 211 153 / 0.74);
  background: rgb(11 72 54 / 0.52);
}

.node-main {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 0.45rem;
}

.node-dot {
  width: 0.45rem;
  height: 0.45rem;
  flex: 0 0 auto;
  border-radius: 999px;
  background: oklch(0.63 0.11 210);
  box-shadow: 0 0 12px rgb(56 189 248 / 0.5);
}

.has-fault .node-dot {
  background: oklch(0.65 0.21 25);
  box-shadow: none;
}

.node-name {
  min-width: 0;
  overflow: hidden;
  color: oklch(0.93 0.012 230);
  font-size: 0.78rem;
  font-weight: 750;
  letter-spacing: 0;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.node-sub {
  color: oklch(0.68 0.03 230);
  font-size: 0.68rem;
  font-weight: 650;
}

.empty-rail {
  padding: 1rem 0.8rem;
  color: oklch(0.66 0.025 230);
  font-size: 0.78rem;
}

.battle-stage {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 26rem;
  overflow: hidden;
  border: 1px solid rgb(149 196 255 / 0.16);
  border-radius: 0.65rem;
  background: rgb(7 15 32 / 0.72);
}

.stage-grid {
  position: absolute;
  inset: 0;
  background:
    linear-gradient(90deg, transparent 49.7%, rgb(148 163 184 / 0.08) 50%, transparent 50.3%),
    linear-gradient(0deg, transparent 49.7%, rgb(148 163 184 / 0.08) 50%, transparent 50.3%);
  opacity: 0.45;
  pointer-events: none;
}

.idle-pulse-field {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  pointer-events: none;
}

.idle-pulse-field span {
  position: absolute;
  width: 13rem;
  height: 13rem;
  border: 1px dashed rgb(125 211 252 / 0.18);
  border-radius: 999px;
}

.idle-pulse-field span:nth-child(2) {
  width: 19rem;
  height: 19rem;
}

.idle-pulse-field span:nth-child(3) {
  width: 25rem;
  height: 25rem;
  border-style: solid;
  opacity: 0.32;
}

.event-path-layer {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.beam,
.beam-break {
  position: absolute;
  top: calc(50% - 1px);
  height: 2px;
  border-radius: 999px;
  opacity: 0;
  transform-origin: left center;
}

.beam-in {
  left: 6%;
  width: 42%;
  background: linear-gradient(90deg, transparent, rgb(251 146 60 / 0.95));
}

.beam-out {
  left: 50%;
  width: 42%;
  background: linear-gradient(90deg, rgb(251 146 60 / 0.96), transparent);
}

.beam-break {
  left: 70%;
  width: 13%;
  border-top: 2px dashed rgb(251 146 60 / 0.72);
}

.impact-ring,
.service-wave,
.system-wave,
.diagnostic-scan {
  position: absolute;
  inset: 50% auto auto 50%;
  width: 8rem;
  height: 8rem;
  border-radius: 999px;
  opacity: 0;
  transform: translate(-50%, -50%);
}

.impact-ring {
  left: 87%;
  border: 1px solid rgb(251 146 60 / 0.86);
}

.service-wave {
  border: 1px solid rgb(244 63 94 / 0.72);
}

.system-wave {
  border: 1px solid rgb(125 211 252 / 0.58);
}

.diagnostic-scan {
  width: 62%;
  height: 55%;
  border: 1px dashed rgb(148 163 184 / 0.58);
  border-radius: 1rem;
}

.event-attack-success .beam-in {
  animation: beam-enter 520ms cubic-bezier(0.16, 1, 0.3, 1) both;
}

.event-attack-success .beam-out {
  animation: beam-hit 760ms cubic-bezier(0.16, 1, 0.3, 1) 420ms both;
}

.event-attack-success .impact-ring {
  animation: impact-hit 720ms cubic-bezier(0.16, 1, 0.3, 1) 940ms both;
}

.event-attack-failed .beam-in {
  background: linear-gradient(90deg, transparent, rgb(251 146 60 / 0.72));
  animation: beam-enter 620ms cubic-bezier(0.16, 1, 0.3, 1) both;
}

.event-attack-failed .beam-out {
  width: 28%;
  background: linear-gradient(90deg, rgb(251 146 60 / 0.68), transparent);
  animation: beam-failed 950ms cubic-bezier(0.16, 1, 0.3, 1) 460ms both;
}

.event-attack-failed .beam-break {
  animation: beam-break 820ms steps(3, end) 880ms both;
}

.event-attack-failed .impact-ring {
  left: 78%;
  border-color: rgb(251 146 60 / 0.42);
  animation: intercept-pulse 700ms cubic-bezier(0.16, 1, 0.3, 1) 980ms both;
}

.event-service-down .service-wave {
  animation: service-alert 1.7s cubic-bezier(0.16, 1, 0.3, 1) both;
}

.event-service-recovered .service-wave {
  border-color: rgb(52 211 153 / 0.72);
  animation: service-recovered 1.45s cubic-bezier(0.16, 1, 0.3, 1) both;
}

.event-system .system-wave,
.event-gamebox-restart .system-wave {
  animation: system-sync 1.25s cubic-bezier(0.16, 1, 0.3, 1) both;
}

.event-checker-error .diagnostic-scan {
  animation: diagnostic-sweep 1.5s cubic-bezier(0.16, 1, 0.3, 1) both;
}

.core-orbit,
.battle-core {
  position: absolute;
  border-radius: 999px;
}

.core-orbit {
  pointer-events: none;
}

.outer-orbit {
  width: 16.5rem;
  height: 16.5rem;
  border: 1px dashed rgb(148 163 184 / 0.24);
}

.inner-orbit {
  width: 12.2rem;
  height: 12.2rem;
  border: 1px solid rgb(148 163 184 / 0.18);
}

.battle-core {
  display: grid;
  place-items: center;
  width: 10.6rem;
  height: 10.6rem;
  background: rgb(8 19 39);
  box-shadow: inset 0 0 0 1px rgb(148 163 184 / 0.08);
}

.core-ring {
  position: absolute;
  inset: 0.38rem;
  border: 1px solid rgb(125 211 252 / 0.32);
  border-radius: inherit;
}

.core-content {
  position: relative;
  z-index: 1;
  display: grid;
  justify-items: center;
  gap: 0.2rem;
  max-width: 8.5rem;
  text-align: center;
}

.core-round-label,
.core-mode {
  color: oklch(0.72 0.06 220);
  font-size: 0.64rem;
  font-weight: 850;
  letter-spacing: 0.13em;
}

.core-round-value {
  color: oklch(0.97 0.018 220);
  font-size: 2.65rem;
  font-weight: 850;
  line-height: 0.96;
  font-variant-numeric: tabular-nums;
}

.core-result {
  color: oklch(0.95 0.03 220);
  font-size: 0.78rem;
  font-weight: 850;
  letter-spacing: 0.05em;
}

.core-summary {
  max-width: 8.2rem;
  overflow: hidden;
  color: oklch(0.71 0.035 230);
  font-size: 0.64rem;
  font-weight: 650;
  line-height: 1.25;
  text-overflow: ellipsis;
}

.is-attack-success .battle-core {
  box-shadow:
    inset 0 0 34px rgb(251 146 60 / 0.2),
    0 0 46px rgb(251 146 60 / 0.18);
}

.is-attack-success .core-ring,
.is-attack-failed .core-ring {
  border-color: rgb(251 146 60 / 0.6);
}

.is-service-down .battle-core {
  animation: core-jitter 180ms steps(2, end) 6;
  box-shadow:
    inset 0 0 34px rgb(244 63 94 / 0.24),
    0 0 42px rgb(244 63 94 / 0.2);
}

.is-service-down .core-ring {
  border-color: rgb(244 63 94 / 0.68);
}

.is-service-recovered .core-ring {
  border-color: rgb(52 211 153 / 0.64);
}

.is-checker-error .core-ring {
  border-color: rgb(192 132 252 / 0.68);
}

.is-system .core-ring,
.is-gamebox-restart .core-ring {
  border-color: rgb(125 211 252 / 0.58);
}

.battlefield-footer {
  display: grid;
  grid-template-columns: minmax(14rem, 0.8fr) minmax(0, 1.2fr);
  gap: 0.75rem;
  border-top: 1px solid rgb(149 196 255 / 0.12);
  padding: 0.75rem 1rem 1rem;
}

.state-strip,
.event-log {
  border: 1px solid rgb(149 196 255 / 0.13);
  border-radius: 0.7rem;
  background: rgb(7 16 34 / 0.42);
}

.state-strip {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 0.75rem;
  padding: 0.65rem 0.75rem;
  color: oklch(0.74 0.035 230);
  font-size: 0.75rem;
  font-weight: 700;
}

.state-strip span:first-child {
  flex: 0 0 auto;
  border-radius: 999px;
  padding: 0.25rem 0.55rem;
  font-size: 0.66rem;
  letter-spacing: 0.08em;
}

.state-strip span:last-child {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tone-idle {
  background: rgb(56 189 248 / 0.12);
  color: oklch(0.8 0.1 215);
}

.tone-attack-success,
.tone-attack-failed {
  background: rgb(251 146 60 / 0.14);
  color: oklch(0.8 0.17 55);
}

.tone-service-down {
  background: rgb(244 63 94 / 0.16);
  color: oklch(0.78 0.17 20);
}

.tone-service-recovered {
  background: rgb(52 211 153 / 0.14);
  color: oklch(0.79 0.12 165);
}

.tone-checker-error {
  background: rgb(192 132 252 / 0.14);
  color: oklch(0.78 0.13 305);
}

.tone-system,
.tone-gamebox-restart {
  background: rgb(125 211 252 / 0.12);
  color: oklch(0.8 0.09 220);
}

.event-log {
  display: grid;
  gap: 0.3rem;
  padding: 0.55rem 0.7rem;
}

.event-log p {
  margin: 0;
  overflow: hidden;
  color: oklch(0.72 0.032 230);
  font-size: 0.72rem;
  font-weight: 650;
  line-height: 1.25;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@keyframes stage-scan {
  0% {
    transform: translateY(-100%);
    opacity: 0;
  }
  38%,
  68% {
    opacity: 0.52;
  }
  100% {
    transform: translateY(100%);
    opacity: 0;
  }
}

@keyframes radar-rotate {
  to {
    transform: rotate(360deg);
  }
}

@keyframes core-breathe {
  0%,
  100% {
    transform: scale(0.98);
    opacity: 0.66;
  }
  50% {
    transform: scale(1.04);
    opacity: 1;
  }
}

@keyframes beam-enter {
  from {
    clip-path: inset(0 100% 0 0);
    opacity: 0;
  }
  18% {
    opacity: 1;
  }
  to {
    clip-path: inset(0 0 0 0);
    opacity: 1;
  }
}

@keyframes beam-hit {
  from {
    clip-path: inset(0 100% 0 0);
    opacity: 0;
  }
  18% {
    opacity: 1;
  }
  78% {
    opacity: 1;
  }
  to {
    clip-path: inset(0 0 0 0);
    opacity: 0;
  }
}

@keyframes beam-failed {
  from {
    clip-path: inset(0 100% 0 0);
    opacity: 0;
  }
  25%,
  62% {
    opacity: 0.78;
  }
  to {
    clip-path: inset(0 0 0 0);
    opacity: 0;
    filter: blur(2px);
  }
}

@keyframes beam-break {
  0%,
  100% {
    opacity: 0;
  }
  35%,
  72% {
    opacity: 0.9;
  }
}

@keyframes impact-hit {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.28);
  }
  38% {
    opacity: 1;
  }
  to {
    opacity: 0;
    transform: translate(-50%, -50%) scale(1.45);
  }
}

@keyframes intercept-pulse {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.5);
  }
  44% {
    opacity: 0.72;
  }
  to {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.9);
  }
}

@keyframes service-alert {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.45);
  }
  22%,
  58% {
    opacity: 0.82;
  }
  to {
    opacity: 0;
    transform: translate(-50%, -50%) scale(2.9);
  }
}

@keyframes service-recovered {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(2.35);
  }
  42% {
    opacity: 0.82;
  }
  to {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.88);
  }
}

@keyframes system-sync {
  from {
    opacity: 0;
    transform: translate(-50%, -50%) scale(0.72);
  }
  36% {
    opacity: 0.72;
  }
  to {
    opacity: 0;
    transform: translate(-50%, -50%) scale(2.1);
  }
}

@keyframes diagnostic-sweep {
  from {
    clip-path: inset(0 100% 0 0);
    opacity: 0;
  }
  24%,
  72% {
    opacity: 0.88;
  }
  to {
    clip-path: inset(0 0 0 0);
    opacity: 0;
  }
}

@keyframes core-jitter {
  0%,
  100% {
    transform: translate(0, 0);
  }
  50% {
    transform: translate(1px, -1px);
  }
}

@keyframes node-alert {
  50% {
    filter: brightness(1.34);
  }
}

@media (max-width: 1100px) {
  .battlefield-grid {
    grid-template-columns: 1fr;
  }

  .node-list {
    max-height: 12rem;
  }

  .battlefield-footer {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  .awd-battlefield-core *,
  .awd-battlefield-core::after {
    animation-duration: 1ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 1ms !important;
  }
}
</style>
