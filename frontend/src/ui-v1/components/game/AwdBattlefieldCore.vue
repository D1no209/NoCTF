<script setup lang="ts">
import type { AttackLogDto } from '@/ui-v1/components/game/AttackLogFeed.vue'
import type { ServiceStatus } from '@/ui-v1/components/game/ServiceStatusGrid.vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card } from '@/ui-v1/components/ui/card'
import { Panel } from '@/ui-v1/components/ui/panel'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Separator } from '@/ui-v1/components/ui/separator'

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

const { t } = useI18n()

const eventQueue = ref<AwdAwarenessEvent[]>([])
const activeEvent = ref<AwdAwarenessEvent | null>(null)
const playedEvents = ref<AwdAwarenessEvent[]>([])
const isPlaying = ref(false)
const playbackKey = ref(0)
const knownEventKeys = new Set<string>()
const serviceState = new Map<string, ServiceStatus['status']>()
const timers = new Set<number>()
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
    return t('awd.battlefield.stateMonitoring')

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
    return event.result === 'success' ? t('awd.battlefield.events.attackSuccess') : t('awd.battlefield.events.attackFailed')
  if (event.type === 'service_down')
    return t('awd.battlefield.events.serviceDown')
  if (event.type === 'service_recovered')
    return t('awd.battlefield.events.serviceRecovered')
  if (event.type === 'round_start')
    return t('awd.battlefield.events.roundStart', { round: event.round ?? displayedRound.value })
  if (event.type === 'flag_refresh')
    return t('awd.battlefield.events.flagRefreshed')
  if (event.type === 'checker_error')
    return t('awd.battlefield.events.checkerError')
  return t('awd.battlefield.events.gameboxRestarting')
}

function eventSummary(event: AwdAwarenessEvent) {
  const challengeName = event.challengeName ?? event.serviceName ?? t('awd.battlefield.defaults.service')

  if (event.type === 'attack') {
    return t('awd.battlefield.summary.attack', {
      attacker: event.attackerTeamName ?? t('awd.battlefield.defaults.attacker'),
      victim: event.victimTeamName ?? t('awd.battlefield.defaults.target'),
      challenge: challengeName,
    })
  }

  if (event.type === 'service_down' || event.type === 'service_recovered' || event.type === 'gamebox_restart')
    return t('awd.battlefield.summary.teamChallenge', { team: event.teamName ?? t('awd.battlefield.defaults.team'), challenge: challengeName })

  if (event.type === 'checker_error')
    return t('awd.battlefield.summary.checkerError', { challenge: challengeName, reason: event.reason ?? '' })

  return event.type === 'flag_refresh'
    ? t('awd.battlefield.summary.flagRotation', { round: event.round ?? displayedRound.value })
    : t('awd.battlefield.summary.round', { round: event.round ?? displayedRound.value })
}

function eventLogLine(event: AwdAwarenessEvent) {
  const time = formatTime(event.timestamp)
  const challengeName = event.challengeName ?? event.serviceName ?? t('awd.battlefield.defaults.service')

  if (event.type === 'attack') {
    const result = event.result === 'success' ? t('awd.battlefield.logLine.resultSuccess') : t('awd.battlefield.logLine.resultFailed')
    return t('awd.battlefield.logLine.attack', {
      time,
      attacker: event.attackerTeamName ?? t('awd.battlefield.defaults.attacker'),
      victim: event.victimTeamName ?? t('awd.battlefield.defaults.target'),
      challenge: challengeName,
      result,
    })
  }

  if (event.type === 'service_down')
    return t('awd.battlefield.logLine.serviceDown', { time, team: event.teamName ?? t('awd.battlefield.defaults.team'), challenge: challengeName })

  if (event.type === 'service_recovered')
    return t('awd.battlefield.logLine.serviceRecovered', { time, team: event.teamName ?? t('awd.battlefield.defaults.team'), challenge: challengeName })

  if (event.type === 'round_start')
    return t('awd.battlefield.logLine.roundStart', { time, round: event.round ?? displayedRound.value })

  if (event.type === 'flag_refresh')
    return t('awd.battlefield.logLine.flagRefresh', { time, round: event.round ?? displayedRound.value })

  if (event.type === 'checker_error')
    return t('awd.battlefield.logLine.checkerError', { time, challenge: challengeName })

  return t('awd.battlefield.logLine.gameboxRestart', { time, team: event.teamName ?? t('awd.battlefield.defaults.team'), challenge: challengeName })
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
    return t('awd.battlefield.healthDown', { count: node.down })
  if (node.unknown > 0)
    return t('awd.battlefield.healthUnknown', { count: node.unknown })
  return t('awd.battlefield.healthAllUp')
}

function serviceHealthLabel(node: ServiceNode) {
  if (node.down > 0)
    return t('awd.battlefield.serviceHealthDown', { down: node.down, total: node.total })
  if (node.unknown > 0)
    return t('awd.battlefield.serviceHealthUnknown', { unknown: node.unknown, total: node.total })
  return t('awd.battlefield.serviceHealthUp', { healthy: node.healthy, total: node.total })
}

function teamNodeClasses(node: TeamNode) {
  if (isActiveTeam(node)) {
    const role = teamRole(node)
    if (role === 'attacker')
      return 'border-neon-web/50 bg-neon-web/10'
    if (role === 'victim')
      return 'border-destructive/50 bg-destructive/10'
    return 'border-primary/50 bg-primary/10'
  }
  if (node.down > 0)
    return 'border-destructive/50 bg-destructive/10'
  return ''
}

function serviceNodeClasses(node: ServiceNode) {
  if (isActiveService(node)) {
    const event = activeEvent.value
    if (event?.type === 'service_down')
      return 'border-destructive/50 bg-destructive/10'
    if (event?.type === 'service_recovered')
      return 'border-[var(--semantic-success-border)] bg-[var(--semantic-success-soft)]'
    if (event?.type === 'checker_error')
      return 'border-neon-misc/50 bg-neon-misc/10'
    return 'border-primary/50 bg-primary/10'
  }
  if (node.down > 0)
    return 'border-destructive/50 bg-destructive/10'
  return ''
}
</script>

<template>
  <Panel
    variant="dark"
    border="default"
    class="relative overflow-hidden rounded-xl"
    aria-live="polite"
  >
    <div class="relative z-10 flex flex-col gap-4 p-4">
      <div class="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p class="text-xs font-bold uppercase tracking-widest text-muted-foreground">
            {{ t('awd.battlefield.kicker') }}
          </p>
          <h2 class="text-base font-extrabold">
            {{ t('awd.battlefield.title') }}
          </h2>
        </div>
        <div class="flex flex-wrap justify-end gap-2">
          <Badge variant="outline" class="text-xs">
            {{ t('awd.battlefield.metricRound') }} <strong class="ml-1 font-mono">{{ displayedRound || 0 }}</strong>
          </Badge>
          <Badge variant="outline" class="text-xs">
            {{ t('awd.battlefield.metricQueue') }} <strong class="ml-1 font-mono">{{ pendingCount }}</strong>
          </Badge>
          <Badge variant="outline" class="text-xs">
            {{ t('awd.battlefield.metricSla') }} <strong class="ml-1 font-mono">{{ healthyRate }}%</strong>
          </Badge>
        </div>
      </div>

      <div class="grid grid-cols-1 gap-3 lg:grid-cols-[1fr_2fr_1fr] min-h-[26rem]">
        <Card class="flex flex-col gap-2 overflow-hidden p-3">
          <div class="flex items-center justify-between text-xs font-bold uppercase tracking-wider text-muted-foreground">
            <span>{{ t('awd.battlefield.railTeams') }}</span>
            <span class="font-mono">{{ teamNodes.length }}</span>
          </div>
          <Separator />
          <div v-if="teamNodes.length === 0" class="text-sm text-muted-foreground">
            {{ t('awd.battlefield.emptyTeams') }}
          </div>
          <div v-else class="flex-1 space-y-2 overflow-y-auto pr-1">
            <Card
              v-for="node in teamNodes"
              :key="node.id"
              class="gap-1 p-2"
              :class="teamNodeClasses(node)"
            >
              <div class="flex items-center gap-2">
                <span
                  class="size-2 shrink-0 rounded-full"
                  :class="node.down > 0 ? 'bg-destructive' : 'bg-primary'"
                />
                <span class="truncate text-sm font-semibold">{{ node.name }}</span>
              </div>
              <span class="text-xs text-muted-foreground">{{ teamHealthLabel(node) }}</span>
            </Card>
          </div>
        </Card>

        <Card class="relative grid min-h-[26rem] place-items-center overflow-hidden">
          <div class="z-10 flex flex-col items-center gap-1 text-center">
            <span class="text-xs font-bold uppercase tracking-widest text-muted-foreground">
              {{ t('awd.battlefield.coreRoundLabel') }}
            </span>
            <strong class="text-5xl font-extrabold leading-none">
              {{ displayedRound || 0 }}
            </strong>
            <Badge variant="secondary" class="mt-1 text-xs uppercase tracking-wider">
              {{ coreMode }}
            </Badge>
            <span class="mt-1 max-w-[16rem] truncate text-sm font-bold">{{ coreResult }}</span>
            <span class="max-w-[16rem] text-xs text-muted-foreground line-clamp-2">
              {{ coreSummary }}
            </span>
          </div>
        </Card>

        <Card class="flex flex-col gap-2 overflow-hidden p-3">
          <div class="flex items-center justify-between text-xs font-bold uppercase tracking-wider text-muted-foreground">
            <span>{{ t('awd.battlefield.railServices') }}</span>
            <span class="font-mono">{{ serviceNodes.length }}</span>
          </div>
          <Separator />
          <div v-if="serviceNodes.length === 0" class="text-sm text-muted-foreground">
            {{ t('awd.battlefield.emptyServices') }}
          </div>
          <div v-else class="flex-1 space-y-2 overflow-y-auto pr-1">
            <Card
              v-for="node in serviceNodes"
              :key="node.id"
              class="gap-1 p-2"
              :class="serviceNodeClasses(node)"
            >
              <div class="flex items-center gap-2">
                <span
                  class="size-2 shrink-0 rounded-full"
                  :class="node.down > 0 ? 'bg-destructive' : 'bg-primary'"
                />
                <span class="truncate text-sm font-semibold">{{ node.name }}</span>
              </div>
              <span class="text-xs text-muted-foreground">{{ serviceHealthLabel(node) }}</span>
            </Card>
          </div>
        </Card>
      </div>

      <div class="grid grid-cols-1 gap-3 lg:grid-cols-[1fr_2fr]">
        <div class="flex min-w-0 items-center gap-3 rounded-lg border border-border bg-muted/50 px-3 py-2">
          <Badge
            :variant="isIdle ? 'outline' : 'secondary'"
            class="shrink-0 text-xs"
          >
            {{ isIdle ? t('awd.battlefield.stateIdle') : t('awd.battlefield.statePlayback') }}
          </Badge>
          <span class="min-w-0 truncate text-xs text-muted-foreground">
            {{ activeEvent ? eventTitle(activeEvent) : t('awd.battlefield.stateMonitoring') }}
          </span>
        </div>
        <Card class="gap-1 overflow-hidden p-2">
          <p v-if="playedEvents.length === 0" class="text-xs text-muted-foreground">
            {{ t('awd.battlefield.emptyPlayed') }}
          </p>
          <p
            v-for="event in playedEvents"
            :key="event.id"
            class="truncate text-xs text-muted-foreground"
          >
            {{ eventLogLine(event) }}
          </p>
        </Card>
      </div>
    </div>
  </Panel>
</template>
