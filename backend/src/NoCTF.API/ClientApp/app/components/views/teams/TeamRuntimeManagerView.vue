<script setup lang="ts">
import { toRefs } from 'vue'
import type { TeamRuntimeManagerViewState } from '~/features/teams/useTeamRuntimeManager'

const viewProps = defineProps<{ state: TeamRuntimeManagerViewState }>()
const { RefreshCw, RuntimeCard, competitionId, items, loading, error, initialized, page, pageCount, total, pageLimit, selected, selectRuntime, challengePath, refresh, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <section aria-labelledby="team-runtimes-title">
    <CardHeader class="flex flex-row items-center justify-between gap-3">
      <CardTitle id="team-runtimes-title" class="text-base">{{ $t('runtime.teamInstancesTitle') }}</CardTitle>
      <Button type="button" size="sm" variant="outline" :disabled="loading" @click="refresh">
        <RefreshCw data-icon="inline-start" />{{ $t('common.label.refresh') }}
      </Button>
    </CardHeader>
    <CardContent class="flex flex-col gap-4">
      <Alert v-if="error" variant="destructive">
        <AlertDescription class="flex flex-wrap items-center justify-between gap-2">
          <span>{{ $message(error.message) }}</span>
          <Button type="button" size="sm" variant="outline" @click="refresh">{{ $t('common.label.retry') }}</Button>
        </AlertDescription>
      </Alert>
      <Skeleton v-if="loading && !initialized" class="h-44 w-full" />
      <Empty v-else-if="!items.length && !error" class="py-8">
        <EmptyHeader>
          <EmptyTitle>{{ $t('runtime.noActiveTeamInstances') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <template v-else-if="items.length">
        <ul class="flex min-w-0 flex-col gap-2" :aria-label="$t('runtime.teamInstancesTitle')">
          <li v-for="item in items" :key="(item.runtime?.id) ?? undefined" class="min-w-0">
            <Button
              type="button"
              :variant="selected?.runtime?.id === item.runtime?.id ? 'secondary' : 'ghost'"
              class="h-auto min-h-14 w-full flex-wrap justify-between gap-2 px-3 py-2 text-left"
              :aria-expanded="selected?.runtime?.id === item.runtime?.id"
              :aria-controls="`team-runtime-${item.runtime?.id}`"
              @click="selectRuntime(item.runtime!.id!)"
            >
              <span class="min-w-0 flex-1 truncate">{{ item.challengeTitle }}</span>
              <Badge variant="outline">{{ runtimeStateLabel(item.runtime?.state) }}</Badge>
              <span v-if="item.runtime?.expiresAt" class="w-full text-xs text-muted-foreground">
                {{ $t('runtime.expiresAt', { time: formatDateTime(item.runtime.expiresAt) }) }}
              </span>
            </Button>
            <div
              v-if="selected?.runtime?.id === item.runtime?.id && item.runtime?.competitionChallengeId"
              :id="`team-runtime-${item.runtime.id}`"
              class="flex min-w-0 flex-col gap-3 px-3 py-4"
            >
              <component
                :is="RuntimeCard"
                :key="item.runtime.competitionChallengeId ?? undefined"
                :competition-id="competitionId"
                :competition-challenge-id="item.runtime.competitionChallengeId"
                controls="full"
                @changed="refresh"
              />
              <Button as-child type="button" size="sm" variant="outline" class="self-start">
                <NuxtLink :to="challengePath(item.runtime.competitionChallengeId)">{{ $t('runtime.openChallenge') }}</NuxtLink>
              </Button>
            </div>
          </li>
        </ul>
        <OffsetPagination
          :page="page"
          :page-count="pageCount"
          :total="total"
          :limit="pageLimit"
          :loading="loading"
          @update:page="loadPage"
          @update:limit="setPageSize"
        />
      </template>
    </CardContent>
  </section>
</template>
