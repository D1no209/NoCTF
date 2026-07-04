<script setup lang="ts">
import type { AwdpScreenEvent } from '@/types/awdpScreen'
import { Bell, CheckCircle2, CircleAlert, Info, RotateCw, XCircle } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  events: AwdpScreenEvent[]
}>()

const PAGE_SIZE = 6
const ROTATE_MS = 6_400
const page = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const operationalEvents = computed(() => props.events.filter(event => event.type !== 'SCORE_UPDATED' && event.type !== 'TEAM_RANK_CHANGED'))
const pageCount = computed(() => Math.max(1, Math.ceil(operationalEvents.value.length / PAGE_SIZE)))
const visibleEvents = computed(() => operationalEvents.value.slice(page.value * PAGE_SIZE, page.value * PAGE_SIZE + PAGE_SIZE))
const pageLabel = computed(() => `${page.value + 1}/${pageCount.value}`)

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
    return 'text-emerald-200 bg-emerald-300/10 border-emerald-300/20'
  if (level === 'warning')
    return 'text-amber-200 bg-amber-300/10 border-amber-300/20'
  if (level === 'danger')
    return 'text-rose-200 bg-rose-300/10 border-rose-300/20'
  return 'text-cyan-200 bg-cyan-300/10 border-cyan-300/20'
}

function eventTypeLabel(type: AwdpScreenEvent['type']) {
  if (type === 'ATTACK_ACCEPTED')
    return 'ATTACK SUCCESS'
  if (type === 'ATTACK_REJECTED')
    return 'ATTACK FAILED'
  if (type === 'DEFENSE_CHECK_PASSED')
    return 'DEFENSE SUCCESS'
  if (type === 'DEFENSE_CHECK_FAILED')
    return 'DEFENSE FAILED'
  if (type === 'SERVICE_ERROR')
    return 'SERVICE ERROR'
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
  <section class="awdp-panel flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-slate-200/10 px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-slate-100">
          Event stream
        </h2>
        <p class="text-xs text-slate-500">
          Real AWDP events · page {{ pageLabel }}
        </p>
      </div>
      <div class="flex items-center gap-2 text-slate-500">
        <RotateCw v-if="pageCount > 1" class="size-3.5 text-cyan-200" />
        <Bell class="size-5 text-cyan-100" />
      </div>
    </div>

    <div v-if="visibleEvents.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      No live events yet.
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
        class="event-row rounded-lg border border-slate-300/10 bg-slate-950/46 p-2.5"
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
        <p class="line-clamp-2 text-xs leading-snug text-slate-200">
          {{ event.message }}
        </p>
        <div class="mt-1.5 flex items-center justify-between text-[10px] text-slate-500">
          <span>Round {{ event.round }}</span>
          <span>{{ event.teamName ?? '-' }} → {{ event.challengeName ?? '-' }}</span>
        </div>
      </article>
    </TransitionGroup>
  </section>
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
</style>
