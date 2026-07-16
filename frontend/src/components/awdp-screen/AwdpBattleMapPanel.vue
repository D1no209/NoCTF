<script setup lang="ts">
import type { AwdpChallengeStatus, AwdpScreenEvent, AwdpTeamScore } from '@/types/awdpScreen'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Activity, RotateCw } from 'lucide-vue-next'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'
import { Separator } from '@/components/ui/separator'
import AwdpStarBattleMap from './AwdpStarBattleMap.vue'

const props = defineProps<{
  teams: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  events: AwdpScreenEvent[]
  currentRound: number
}>()

const { t } = useI18n()

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

const ROTATE_MS = 3_400
const activeIndex = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const senseEvents = computed(() => props.events
  .map(toSenseEvent)
  .filter((event): event is SenseEvent => Boolean(event))
  .slice(0, 24))

const currentEvent = computed(() => senseEvents.value[activeIndex.value] ?? null)
const currentRoundLabel = computed(() => currentEvent.value?.round || props.currentRound)

const accent = computed(() => {
  if (!currentEvent.value) {
    return {
      stroke: 'stroke-slate-600',
      strokeDim: 'stroke-slate-400/50',
      fill: 'fill-slate-600',
      border: 'border-slate-400/30',
      resultText: 'text-slate-800',
      shadow: 'shadow-xl',
      dot: 'bg-slate-400',
    }
  }
  switch (`${currentEvent.value.mode}-${currentEvent.value.result}`) {
    case 'attack-success':
      return {
        stroke: 'stroke-cyan-600',
        strokeDim: 'stroke-cyan-400/50',
        fill: 'fill-cyan-600',
        border: 'border-cyan-500/30',
        resultText: 'text-cyan-800',
        shadow: 'shadow-[0_0_38px_rgba(8,145,178,0.14)]',
        dot: 'bg-cyan-500',
      }
    case 'attack-failed':
    case 'defense-failed':
      return {
        stroke: 'stroke-amber-600',
        strokeDim: 'stroke-amber-400/50',
        fill: 'fill-amber-600',
        border: 'border-amber-500/30',
        resultText: 'text-amber-800',
        shadow: 'shadow-[0_0_38px_rgba(217,119,6,0.14)]',
        dot: 'bg-amber-500',
      }
    case 'defense-success':
      return {
        stroke: 'stroke-emerald-600',
        strokeDim: 'stroke-emerald-400/50',
        fill: 'fill-emerald-600',
        border: 'border-emerald-500/30',
        resultText: 'text-emerald-800',
        shadow: 'shadow-[0_0_38px_rgba(5,150,105,0.14)]',
        dot: 'bg-emerald-500',
      }
    case 'service-error':
      return {
        stroke: 'stroke-rose-600',
        strokeDim: 'stroke-rose-400/50',
        fill: 'fill-rose-600',
        border: 'border-rose-500/30',
        resultText: 'text-rose-800',
        shadow: 'shadow-[0_0_38px_rgba(225,29,72,0.14)]',
        dot: 'bg-rose-500',
      }
    default:
      return {
        stroke: 'stroke-slate-600',
        strokeDim: 'stroke-slate-400/50',
        fill: 'fill-slate-600',
        border: 'border-slate-400/30',
        resultText: 'text-slate-800',
        shadow: 'shadow-xl',
        dot: 'bg-slate-400',
      }
  }
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
    return buildSenseEvent(event, 'attack', 'success', t('awdpScreen.battleMap.events.attackSuccess'))
  }
  if (event.type === 'ATTACK_REJECTED') {
    return buildSenseEvent(event, 'attack', 'failed', t('awdpScreen.battleMap.events.attackFailed'))
  }
  if (event.type === 'DEFENSE_CHECK_PASSED') {
    return buildSenseEvent(event, 'defense', 'success', t('awdpScreen.battleMap.events.defenseSuccess'))
  }
  if (event.type === 'DEFENSE_CHECK_FAILED') {
    return buildSenseEvent(event, 'defense', 'failed', t('awdpScreen.battleMap.events.defenseFailed'))
  }
  if (event.type === 'SERVICE_ERROR') {
    return buildSenseEvent(event, 'service', 'error', t('awdpScreen.battleMap.events.serviceError'))
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
    teamName: event.teamName ?? t('awdpScreen.battleMap.defaults.team'),
    challengeId: event.challengeId,
    challengeName: event.challengeName ?? t('awdpScreen.battleMap.defaults.service'),
    round: event.round || props.currentRound,
    createdAt: event.createdAt,
  }
}
</script>

<template>
  <Panel variant="default" class="flex min-h-0 flex-col" :class="currentEvent ? accent.border : ''">
    <div class="flex items-center justify-between gap-4 px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-slate-900">
          {{ t('awdpScreen.battleMap.title') }}
        </h2>
        <p class="text-xs text-slate-500">
          {{ t('awdpScreen.battleMap.subtitle') }}
        </p>
      </div>
      <Badge
        variant="outline"
        class="inline-flex items-center gap-1.5 rounded-md border-slate-300/60 bg-white/60 px-2 py-1 text-[0.68rem] font-semibold uppercase tracking-wide text-slate-600"
        :class="currentEvent ? accent.border : ''"
      >
        <span class="size-2 rounded-full" :class="currentEvent ? accent.dot : 'bg-slate-400'" />
        <span>{{ currentEvent?.label ?? t('awdpScreen.battleMap.waitingEvent') }}</span>
      </Badge>
    </div>

    <Separator class="bg-slate-300/40" />

    <div class="relative min-h-[22rem] flex-1 overflow-hidden">
      <AwdpStarBattleMap
        class="absolute inset-0"
        :teams="teams"
        :challenges="challenges"
        :events="events"
        :current-round="currentRound"
        :active-event-id="currentEvent?.id"
      />

      <div class="absolute bottom-3 left-1/2 z-20 w-auto max-w-[90%] -translate-x-1/2">
        <Card
          :decorated="false"
          class="border border-slate-300/30 bg-white/50 px-4 py-2 text-center shadow-sm"
        >
          <div class="text-[10px] font-bold uppercase tracking-wide text-slate-500">
            {{ t('awdpScreen.battleMap.roundPrefix') }} {{ currentRoundLabel }}
          </div>
          <div
            class="mt-0.5 text-sm font-bold"
            :class="currentEvent ? accent.resultText : 'text-slate-700'"
          >
            {{ currentEvent?.label ?? t('awdpScreen.battleMap.waitingEvent') }}
          </div>
          <div class="mt-0.5 truncate text-xs text-slate-600">
            {{ currentEvent?.teamName ?? t('awdpScreen.eventStream.fallbackDash') }} → {{ currentEvent?.challengeName ?? t('awdpScreen.eventStream.fallbackDash') }}
          </div>
        </Card>
      </div>

      <div
        v-if="!currentEvent"
        class="absolute right-4 bottom-4 z-30 max-w-xs rounded-lg border border-slate-400/20 bg-white/80 p-3 text-xs text-slate-500"
      >
        {{ t('awdpScreen.battleMap.empty') }}
      </div>
    </div>

    <Separator class="bg-slate-300/40" />

    <div class="flex items-center justify-between gap-4 px-4 py-2 text-[0.68rem] font-extrabold uppercase text-slate-500">
      <span class="inline-flex items-center gap-2">
        <Activity class="size-3.5" />
        {{ t('awdpScreen.battleMap.footer.resultPlayback') }}
      </span>
      <span class="inline-flex items-center gap-2">
        <RotateCw class="size-3.5" />
        {{ t('awdpScreen.battleMap.footer.liveEventCycle') }}
      </span>
    </div>
  </Panel>
</template>

<style scoped>
@media (prefers-reduced-motion: reduce) {
  * {
    animation: none !important;
  }
}
</style>
