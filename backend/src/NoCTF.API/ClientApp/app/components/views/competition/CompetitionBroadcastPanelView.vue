<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionBroadcastPanelViewState } from '~/features/competition/useCompetitionBroadcastPanel'

const viewProps = defineProps<{ state: CompetitionBroadcastPanelViewState }>()
const { Megaphone, items, loading, error, refreshLatest, fill } = toRefs(viewProps.state)
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
        <h2 id="competition-broadcast-title" class="text-sm font-semibold">{{ $t('ui.competitionFeed') }}</h2>
      </div>
      <span class="text-[0.6875rem] font-medium tracking-wide text-muted-foreground">{{ $t('ui.live') }}</span>
    </header>

    <div v-if="loading" class="flex flex-col gap-3 p-4">
      <Skeleton v-for="index in 4" :key="index" class="h-12 w-full" />
    </div>
    <div v-else-if="error" class="p-4">
      <p class="text-xs leading-5 text-destructive">{{ $message(error) }}</p>
      <Button variant="ghost" size="sm" class="mt-2 px-0" @click="refreshLatest">{{ $t('ui.reload') }}</Button>
    </div>
    <div v-else-if="!items.length" class="px-4 py-8 text-center">
      <p class="text-sm text-muted-foreground">{{ $t('ui.thereIsNoMatchReportYet') }}</p>
      <p class="mt-1 text-xs text-muted-foreground/80">{{ $t('ui.bloodListQuestionsAndDisciplineInformationWillBeUpdatedHere') }}</p>
    </div>
    <TransitionGroup data-scroll-surface
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
