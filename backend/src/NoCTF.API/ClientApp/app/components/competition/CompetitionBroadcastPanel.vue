<script setup lang="ts">
import { Megaphone } from '@lucide/vue'
import { listCompetitionEvents } from '~/api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '~/api'
import { createTrailingRefresh } from '~/lib/latest-page-refresh'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

const props = withDefaults(defineProps<{
  competitionId: string
  fill?: boolean
}>(), {
  fill: false,
})
const ctx = inject(competitionContextKey)!
const items = ref<CompetitionEvent[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const initialized = ref(false)
let loadedStatus: string | null = null

async function load(): Promise<void> {
  const competition = ctx.competition.value
  if (!competition) return
  const status = competition.status ?? null
  const startAt = competition.startTime ? new Date(competition.startTime).getTime() : null
  const now = Date.now()
  if (status === 'Finished' && initialized.value && loadedStatus === status) return
  if (startAt === null || now < startAt || status === 'Draft' || status === 'Visible' || status === 'Published') {
    items.value = []
    error.value = null
    loading.value = false
    initialized.value = true
    loadedStatus = status
    return
  }
  const initialLoad = !initialized.value
  if (initialLoad) {
    loading.value = true
    error.value = null
  }
  const queryEnd = status === 'Finished' && competition.endTime
    ? Math.min(now, new Date(competition.endTime).getTime())
    : now
  const from = new Date(Math.max(startAt, queryEnd - 30 * 24 * 60 * 60 * 1000)).toISOString()
  const { data, error: requestError } = await listCompetitionEvents({
    path: { competitionId: props.competitionId },
    query: {
      from,
      to: new Date(queryEnd).toISOString(),
      kinds: competitionBroadcastKinds,
      limit: 10,
    },
  })
  if (requestError || !data) {
    if (initialLoad)
      error.value = parseApiError(requestError, translate("加载赛事播报失败")).message
    loading.value = false
    return
  }
  items.value = mergeCompetitionBroadcasts(items.value, data.items ?? [])
  initialized.value = true
  loadedStatus = status
  loading.value = false
  error.value = null
}

const refreshLatest = createTrailingRefresh(load)

watch(
  () => ctx.competition.value,
  competition => {
    if (competition) void refreshLatest()
  },
  { immediate: true },
)

let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(props.competitionId, {
    competitionEventChanged: notification => {
      if (!isCompetitionBroadcastKind(notification.kind)) return
      void refreshLatest()
    },
    onReconnected: () => void refreshLatest(),
  })
})
onUnmounted(() => unwatch?.())
</script>

<template>
  <aside
    class="rounded-xl border bg-card"
    :class="fill ? 'flex min-h-0 flex-col' : 'xl:sticky xl:top-24'"
    aria-labelledby="competition-broadcast-title"
  >
    <header class="flex items-center justify-between gap-3 border-b px-4 py-3">
      <div class="flex items-center gap-2">
        <Megaphone class="size-4 text-primary" aria-hidden="true" />
        <h2 id="competition-broadcast-title" class="text-sm font-semibold">{{ $t('赛事播报') }}</h2>
      </div>
      <span class="text-[0.6875rem] font-medium tracking-wide text-muted-foreground">LIVE</span>
    </header>

    <div v-if="loading" class="flex flex-col gap-3 p-4">
      <Skeleton v-for="index in 4" :key="index" class="h-12 w-full" />
    </div>
    <div v-else-if="error" class="p-4">
      <p class="text-xs leading-5 text-destructive">{{ error }}</p>
      <Button variant="ghost" size="sm" class="mt-2 px-0" @click="refreshLatest">{{ $t('重新加载') }}</Button>
    </div>
    <div v-else-if="!items.length" class="px-4 py-8 text-center">
      <p class="text-sm text-muted-foreground">{{ $t('暂无赛事播报') }}</p>
      <p class="mt-1 text-xs text-muted-foreground/80">{{ $t('血榜、题目与纪律消息会在这里更新') }}</p>
    </div>
    <TransitionGroup
      v-else
      tag="ol"
      name="broadcast"
      class="divide-y overflow-y-auto"
      :class="fill ? 'min-h-0 flex-1' : 'max-h-[32rem]'"
    >
      <li v-for="event in items" :key="competitionBroadcastIdentity(event)">
        <NuxtLink
          v-if="competitionBroadcastTargetPath(event)"
          :to="competitionBroadcastTargetPath(event)!"
          class="group block px-4 py-3 transition-colors hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
        >
          <p class="text-xs leading-5 text-foreground group-hover:text-primary">
            {{ competitionBroadcastText(event) }}
          </p>
          <time class="mt-1 block font-mono text-[0.6875rem] text-muted-foreground" :datetime="event.occurredAt">
            {{ formatDateTime(event.occurredAt) }}
          </time>
        </NuxtLink>
        <div v-else class="px-4 py-3">
          <p class="text-xs leading-5 text-foreground">{{ competitionBroadcastText(event) }}</p>
          <time class="mt-1 block font-mono text-[0.6875rem] text-muted-foreground" :datetime="event.occurredAt">
            {{ formatDateTime(event.occurredAt) }}
          </time>
        </div>
      </li>
    </TransitionGroup>
  </aside>
</template>

<style scoped>
.broadcast-enter-active {
  transition:
    opacity 220ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 220ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 700ms cubic-bezier(0.16, 1, 0.3, 1);
}

.broadcast-enter-from {
  opacity: 0;
  transform: translateY(-0.4rem);
  background-color: color-mix(in oklch, var(--primary) 10%, transparent);
}

.broadcast-move {
  transition: transform 220ms cubic-bezier(0.16, 1, 0.3, 1);
}

@media (prefers-reduced-motion: reduce) {
  .broadcast-enter-active,
  .broadcast-move {
    transition: none;
  }
}
</style>
