<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionBroadcastPanelViewState } from '~/features/competition/useCompetitionBroadcastPanel'

const viewProps = defineProps<{ state: CompetitionBroadcastPanelViewState }>()
const { Megaphone, items, loading, loadingMore, hasMore, historyError, error, broadcastMotionAttributes, refreshLatest, loadMore, fill, emptyDescriptionKey } = toRefs(viewProps.state)
</script>

<template>
  <Card
    v-if="state.canReadBroadcasts"
    as="aside"
    size="sm"
    class="gap-0 py-0"
    :class="fill ? 'flex min-h-0 flex-col' : 'xl:sticky xl:top-24'"
    aria-labelledby="competition-broadcast-title"
  >
    <CardHeader class="flex flex-row items-center justify-between gap-3 px-4 py-3">
      <div class="flex items-center gap-2">
        <Megaphone class="size-4 text-primary" aria-hidden="true" />
        <CardTitle id="competition-broadcast-title">{{ $t('competitions.label.competitionFeed') }}</CardTitle>
      </div>
      <span class="text-[0.6875rem] font-medium tracking-wide text-muted-foreground">{{ $t('competitions.label.live') }}</span>
    </CardHeader>

    <CardContent v-if="loading" class="flex flex-col gap-3 px-4 pb-4">
      <Skeleton v-for="index in 4" :key="index" class="h-12 w-full" />
    </CardContent>
    <CardContent v-else-if="error" class="px-4 pb-4">
      <p class="text-xs leading-5 text-destructive">{{ $message(error) }}</p>
      <Button variant="ghost" size="sm" class="mt-2 px-0" @click="refreshLatest">{{ $t('common.label.reload') }}</Button>
    </CardContent>
    <CardContent v-else-if="!items.length && !hasMore" class="px-4 pb-8 pt-5 text-center">
      <p class="text-sm text-muted-foreground">{{ $t('competitions.competitionBroadcast.description.thereMatchReportYet') }}</p>
      <p class="mt-1 text-xs text-muted-foreground/80">{{ $t(emptyDescriptionKey) }}</p>
    </CardContent>
    <ScrollSurface axis="y"
      v-else
      class="overflow-y-auto"
      :class="fill ? 'min-h-0 flex-1' : 'max-h-[50dvh]'"
      :aria-label="$t('competitions.label.competitionFeed')"
      :aria-busy="loadingMore"
    >
      <ol class="divide-y">
      <li
        v-for="event in items"
        :key="competitionBroadcastIdentity(event)"
        v-bind="broadcastMotionAttributes(event)"
      >
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
      </ol>
      <ScrollLoadTrigger :enabled="hasMore && !historyError" :loading="loadingMore" @load="loadMore" class="px-4 py-3 text-center">
        <p v-if="historyError" class="text-xs text-destructive" role="status">{{ $message(historyError) }}</p>
        <Button v-if="hasMore" variant="ghost" size="sm" :disabled="loadingMore" @click="loadMore">
          <Spinner v-if="loadingMore" data-icon="inline-start" />
          {{ historyError ? $t('common.label.retry') : loadingMore ? $t('common.label.loading') : $t('competitions.competitionBroadcast.label.loadOlder') }}
        </Button>
        <p v-else class="text-xs text-muted-foreground" role="status">{{ $t('competitions.competitionBroadcast.label.historyComplete') }}</p>
      </ScrollLoadTrigger>
    </ScrollSurface>
  </Card>
</template>
