<script setup lang="ts">
import type { AwdpScreenEvent } from '@/types/awdpScreen'
import { Bell, CheckCircle2, CircleAlert, Info, RotateCw, XCircle } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  events: AwdpScreenEvent[]
}>()

const PAGE_SIZE = 5
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
    return 'event-success'
  if (level === 'warning')
    return 'event-warning'
  if (level === 'danger')
    return 'event-danger'
  return 'event-info'
}

function eventTypeLabel(type: AwdpScreenEvent['type']) {
  if (type === 'ATTACK_ACCEPTED')
    return 'BREAK SUCCESS'
  if (type === 'ATTACK_REJECTED')
    return 'BREAK FAILED'
  if (type === 'DEFENSE_CHECK_PASSED')
    return 'FIX SUCCESS'
  if (type === 'DEFENSE_CHECK_FAILED')
    return 'FIX FAILED'
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

function eventMessage(event: AwdpScreenEvent) {
  const team = event.teamName ?? 'Team'
  const challenge = event.challengeName ?? 'service'

  if (event.type === 'ATTACK_ACCEPTED')
    return `${team} broke ${challenge}`
  if (event.type === 'ATTACK_REJECTED')
    return `${team} break failed on ${challenge}`
  if (event.type === 'DEFENSE_CHECK_PASSED')
    return `${team} fix verified on ${challenge}`
  if (event.type === 'DEFENSE_CHECK_FAILED')
    return `${team} fix failed on ${challenge}`
  if (event.type === 'SERVICE_ERROR')
    return `${team} service error on ${challenge}`
  if (event.type === 'PATCH_UPLOADED')
    return `${team} uploaded patch for ${challenge}`
  if (event.type === 'DEFENSE_REQUESTED')
    return `${team} requested fix validation on ${challenge}`
  if (event.type === 'INSTANCE_CREATED')
    return `${team} created ${challenge} instance`
  if (event.type === 'ROUND_STARTED')
    return `Round ${event.round} started`
  if (event.type === 'ROUND_ENDED')
    return `Round ${event.round} ended`
  return event.message
}
</script>

<template>
  <section class="awdp-panel event-panel">
    <div class="event-head">
      <div>
        <h2>
          Event stream
        </h2>
        <p>
          Real AWDP events, page {{ pageLabel }}
        </p>
      </div>
      <div class="event-head-icons">
        <RotateCw v-if="pageCount > 1" class="size-3.5" />
        <Bell class="size-4" />
      </div>
    </div>

    <div v-if="visibleEvents.length === 0" class="event-empty">
      No live events yet.
    </div>

    <TransitionGroup
      v-else
      name="event-row"
      tag="div"
      class="event-list"
    >
      <article
        v-for="event in visibleEvents"
        :key="event.id"
        class="event-row"
      >
        <div class="event-row-top">
          <span
            class="event-badge"
            :class="levelClass(event.level)"
          >
            <component :is="levelIcon(event.level)" class="size-3.5 shrink-0" />
            <span class="truncate">{{ eventTypeLabel(event.type) }}</span>
          </span>
          <span class="event-time">{{ formatTime(event.createdAt) }}</span>
        </div>
        <p class="event-message">
          {{ eventMessage(event) }}
        </p>
        <div class="event-meta">
          <span>Round {{ event.round }}</span>
          <span>{{ event.teamName ?? '-' }} → {{ event.challengeName ?? '-' }}</span>
        </div>
      </article>
    </TransitionGroup>
  </section>
</template>

<style scoped>
.event-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.event-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  border-bottom: 1px solid var(--awdp-screen-border);
  padding: 0.78rem 0.9rem 0.68rem;
}

.event-head h2 {
  margin: 0;
  color: var(--sidebar-foreground);
  font-size: 0.82rem;
  font-weight: 850;
  text-transform: uppercase;
}

.event-head p {
  margin: 0.16rem 0 0;
  color: color-mix(in oklch, var(--sidebar-foreground) 44%, transparent);
  font-size: 0.68rem;
}

.event-head-icons {
  display: inline-flex;
  align-items: center;
  gap: 0.42rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 52%, transparent);
}

.event-empty {
  display: grid;
  flex: 1;
  place-items: center;
  padding: 1.5rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 45%, transparent);
  font-size: 0.82rem;
  text-align: center;
}

.event-list {
  min-height: 0;
  flex: 1;
  padding: 0.55rem;
}

.event-row {
  min-height: calc((100% - 0.48rem * 4) / 5);
  margin-bottom: 0.48rem;
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 10%, transparent);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 44%, var(--awdp-screen-bg));
  padding: 0.54rem 0.62rem;
}

.event-row:last-child {
  margin-bottom: 0;
}

.event-row-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.7rem;
}

.event-badge {
  display: inline-flex;
  min-width: 0;
  align-items: center;
  gap: 0.36rem;
  border: 1px solid currentColor;
  border-radius: var(--radius-sm);
  padding: 0.22rem 0.42rem;
  font-size: 0.58rem;
  font-weight: 850;
}

.event-success {
  color: var(--awdp-fix);
  background: color-mix(in oklch, var(--awdp-fix) 9%, transparent);
}

.event-warning {
  color: var(--awdp-warn);
  background: color-mix(in oklch, var(--awdp-warn) 9%, transparent);
}

.event-danger {
  color: var(--awdp-error);
  background: color-mix(in oklch, var(--awdp-error) 10%, transparent);
}

.event-info {
  color: color-mix(in oklch, var(--sidebar-foreground) 72%, transparent);
  background: color-mix(in oklch, var(--sidebar-foreground) 7%, transparent);
}

.event-time {
  flex: 0 0 auto;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.62rem;
  font-variant-numeric: tabular-nums;
}

.event-message {
  display: -webkit-box;
  margin: 0.42rem 0 0;
  overflow: hidden;
  color: var(--sidebar-foreground);
  font-size: 0.74rem;
  font-weight: 700;
  line-height: 1.22;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.event-meta {
  display: flex;
  justify-content: space-between;
  gap: 0.7rem;
  margin-top: 0.42rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.58rem;
}

.event-meta span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

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
