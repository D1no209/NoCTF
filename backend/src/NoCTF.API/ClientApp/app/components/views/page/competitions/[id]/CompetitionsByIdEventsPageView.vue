<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdEventsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdEventsPage'

const viewProps = defineProps<{ state: CompetitionsByIdEventsPageViewState }>()
const { hasStaffHistory, historyScopeError, kind, kindOptions, items, loading, error, hasMore, initialized, loadMore, reload, levelVariant, levelLabel } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-2">
      <div class="flex items-center gap-2">
        <Select v-model="kind">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('competitions.label.updates')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ translate(option.label) }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Badge variant="outline">
          {{ $t(hasStaffHistory ? 'common.label.fullHistory' : 'common.label.lastDays') }}
        </Badge>
      </div>
      <Button variant="outline" size="sm" @click="reload">{{ $t('common.label.refresh') }}</Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>

    <Alert v-if="historyScopeError" variant="destructive">
      <AlertDescription>{{ $message(historyScopeError) }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 6" :key="i" class="h-12 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('competitions.label.newsYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('competitions.competitionsBy.description.announcementsTopicReleasesBlood') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <ul v-else class="flex flex-col gap-2">
      <li
        v-for="event in items"
        :key="event.id"
        class="flex items-start gap-3 rounded-md border px-3 py-2"
      >
        <Badge :variant="levelVariant(event.level)" class="mt-0.5 shrink-0">
          {{ levelLabel(event.level) }}
        </Badge>
        <div class="flex min-w-0 flex-col gap-0.5">
          <p class="text-sm">{{ competitionEventText(event) }}</p>
          <p class="font-mono text-xs text-muted-foreground tabular-nums">{{ formatDateTime(event.occurredAt) }}</p>
        </div>
      </li>
    </ul>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('common.label.load') }} </Button>
    </div>
  </div>
</template>
