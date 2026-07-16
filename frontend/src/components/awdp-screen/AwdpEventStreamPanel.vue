<script setup lang="ts">
import type { AwdpScreenEvent } from '@/types/awdpScreen'
import { Bell, CheckCircle2, CircleAlert, Info, RotateCw, XCircle } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Panel } from '@/components/ui/panel'
import AwdpEventFilter from './AwdpEventFilter.vue'

const props = defineProps<{
  events: AwdpScreenEvent[]
}>()

const { t } = useI18n()
const PAGE_SIZE = 6
const ROTATE_MS = 6_400
const page = ref(0)
const filter = ref('all')
let rotateTimer: ReturnType<typeof setInterval> | null = null

function matchesFilter(event: AwdpScreenEvent, activeFilter: string) {
  if (activeFilter === 'all')
    return true
  if (activeFilter === 'attack')
    return event.type.startsWith('ATTACK')
  if (activeFilter === 'defense')
    return event.type === 'DEFENSE_REQUESTED' || event.type === 'PATCH_UPLOADED' || event.type.startsWith('DEFENSE_CHECK')
  if (activeFilter === 'service')
    return event.type === 'SERVICE_ERROR'
  if (activeFilter === 'system')
    return event.type === 'ROUND_STARTED' || event.type === 'ROUND_ENDED' || event.type === 'SCORE_UPDATED' || event.type === 'TEAM_RANK_CHANGED' || event.type === 'CHALLENGE_SELECTED' || event.type === 'INSTANCE_CREATED'
  return true
}

const filteredEvents = computed(() => props.events.filter(event => matchesFilter(event, filter.value)))
const pageCount = computed(() => Math.max(1, Math.ceil(filteredEvents.value.length / PAGE_SIZE)))
const visibleEvents = computed(() => filteredEvents.value.slice(page.value * PAGE_SIZE, page.value * PAGE_SIZE + PAGE_SIZE))
const pageLabel = computed(() => `${page.value + 1}/${pageCount.value}`)

watch(filter, () => {
  page.value = 0
})

watch(pageCount, (count) => {
  page.value = Math.min(page.value, count - 1)
})

onMounted(() => {
  rotateTimer = setInterval(() => {
    if (pageCount.value > 1)
      page.value = (page.value + 1) % pageCount.value
  }, ROTATE_MS)
})

onUnmounted(() => {
  if (rotateTimer)
    clearInterval(rotateTimer)
})

function levelIcon(level: AwdpScreenEvent['level']) {
  if (level === 'success')
    return CheckCircle2
  if (level === 'warning')
    return CircleAlert
  if (level === 'danger')
    return XCircle
  return Info
}

function levelClass(level: AwdpScreenEvent['level']) {
  if (level === 'success')
    return 'text-emerald-700 bg-emerald-100/60 border-emerald-300/60'
  if (level === 'warning')
    return 'text-amber-700 bg-amber-100/60 border-amber-300/60'
  if (level === 'danger')
    return 'text-rose-700 bg-rose-100/60 border-rose-300/60'
  return 'text-cyan-700 bg-cyan-100/60 border-cyan-300/60'
}

function eventTypeLabel(type: AwdpScreenEvent['type']) {
  if (type === 'ATTACK_ACCEPTED')
    return t('awdpScreen.battleMap.events.attackSuccess')
  if (type === 'ATTACK_REJECTED')
    return t('awdpScreen.battleMap.events.attackFailed')
  if (type === 'DEFENSE_CHECK_PASSED')
    return t('awdpScreen.battleMap.events.defenseSuccess')
  if (type === 'DEFENSE_CHECK_FAILED')
    return t('awdpScreen.battleMap.events.defenseFailed')
  if (type === 'SERVICE_ERROR')
    return t('awdpScreen.battleMap.events.serviceError')
  return type.replace(/_/g, ' ')
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <Panel variant="default" class="flex min-h-0 flex-col">
    <div class="flex flex-col gap-2.5 border-b border-slate-300/40 px-4 py-3">
      <div class="flex items-center justify-between gap-4">
        <div>
          <h2 class="text-sm font-semibold uppercase text-slate-900">
            {{ t('awdpScreen.eventStream.title') }}
          </h2>
          <p class="text-xs text-slate-500">
            {{ t('awdpScreen.eventStream.subtitle', { page: pageLabel }) }}
          </p>
        </div>
        <div class="flex items-center gap-2 text-slate-500">
          <RotateCw v-if="pageCount > 1" class="size-3.5 text-cyan-600" />
          <Bell class="size-5 text-cyan-600" />
        </div>
      </div>
      <AwdpEventFilter v-model="filter" />
    </div>

    <div v-if="visibleEvents.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      {{ t('awdpScreen.eventStream.empty') }}
    </div>

    <TransitionGroup
      v-else
      name="event-row"
      tag="div"
      class="min-h-0 flex-1 space-y-1.5 px-2.5 py-2.5"
    >
      <article
        v-for="event in visibleEvents"
        :key="event.id"
        class="event-row rounded-lg border border-slate-300/40 bg-white/60 p-2.5"
      >
        <div class="mb-1.5 flex items-center justify-between gap-2">
          <span
            class="inline-flex min-w-0 items-center gap-1.5 rounded-md border px-2 py-1 text-[10px] font-bold uppercase"
            :class="levelClass(event.level)"
          >
            <component :is="levelIcon(event.level)" class="size-3.5 shrink-0" />
            <span class="truncate">{{ eventTypeLabel(event.type) }}</span>
          </span>
          <span class="font-mono text-[11px] text-slate-500">{{ formatTime(event.createdAt) }}</span>
        </div>
        <p class="line-clamp-2 text-xs leading-snug text-slate-800">
          {{ event.message }}
        </p>
        <div class="mt-1.5 flex items-center justify-between text-[10px] text-slate-500">
          <span>{{ t('awdpScreen.eventStream.roundPrefix', { round: event.round }) }}</span>
          <span>{{ event.teamName ?? t('awdpScreen.eventStream.fallbackDash') }} → {{ event.challengeName ?? t('awdpScreen.eventStream.fallbackDash') }}</span>
        </div>
      </article>
    </TransitionGroup>
  </Panel>
</template>

<style scoped>
.event-row-enter-active,
.event-row-leave-active {
  transition:
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.event-row-enter-from,
.event-row-leave-to {
  opacity: 0;
  transform: translateX(12px);
}

@media (prefers-reduced-motion: reduce) {
  .event-row-enter-active,
  .event-row-leave-active {
    transition: none;
  }
}
</style>
