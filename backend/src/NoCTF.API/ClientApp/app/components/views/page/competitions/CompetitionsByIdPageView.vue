<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdPageViewState } from '~/features/routes/competitions/useCompetitionsByIdPage'

const viewProps = defineProps<{ state: CompetitionsByIdPageViewState }>()
const { isOverview, isControlScreen, isWriteUpReview, isProgression, competitionId, myTeam, teamBanned, teamLoading, CompetitionTeamBanScreen, competition, myStanding, standingLoading, teamLoadError, standingError, error, refreshMyTeam, refreshMyStanding, CompetitionCountdown, LifecycleBadge } = toRefs(viewProps.state)
</script>

<template>
  <NuxtPage v-if="isOverview || isControlScreen" />
  <NuxtPage v-else-if="teamBanned && myTeam?.id">
    <component :is="CompetitionTeamBanScreen" :competition-id="competitionId" :team-id="myTeam.id" @refresh-team="refreshMyTeam" />
  </NuxtPage>

  <div
    v-else-if="competition && !teamLoading"
    data-contained-workspace-page
    :data-progression-route="isProgression"
    class="mx-auto flex w-full max-w-[120rem] flex-col"
    :class="isWriteUpReview ? 'p-0' : 'gap-4 px-3 py-4 md:px-5'"
  >
    <div v-if="!isWriteUpReview" class="flex flex-col justify-between gap-3 border-b pb-4 sm:flex-row sm:items-start">
      <div class="relative isolate flex min-w-0 flex-1 flex-col gap-1">
        <TypeWatermark v-if="!isProgression" :text="gameModeLabel(competition.mode)" class="text-primary" />
        <span class="sr-only">{{ gameModeLabel(competition.mode) }}</span>
        <div class="relative z-10 flex flex-wrap items-center gap-x-3 gap-y-2">
          <h1 class="text-display text-2xl md:text-3xl">{{ competition.title }}</h1>
          <component :is="LifecycleBadge" :status="competition.status" />
        </div>
        <div v-if="!isProgression" class="relative z-10 flex flex-wrap items-center gap-x-3 gap-y-1 font-mono text-xs tabular-nums md:text-sm">
          <span class="text-muted-foreground">
            {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
          </span>
          <component :is="CompetitionCountdown"
            :start-time="competition.startTime"
            :end-time="competition.endTime"
            :status="competition.status"
            class="font-medium text-primary"
          />
        </div>
      </div>

      <div v-if="standingLoading" class="flex shrink-0 gap-2" :aria-label="$t('competitions.label.loadingTeamStanding')">
        <Skeleton class="h-12 w-24" />
        <Skeleton class="h-12 w-28" />
      </div>
      <dl v-else-if="myStanding" class="flex shrink-0 divide-x rounded-lg border bg-card/60">
        <div class="min-w-24 px-4 py-2 text-right">
          <dt class="text-xs text-muted-foreground">{{ $t('competitions.label.teamRank') }}</dt>
          <dd class="font-mono text-lg font-semibold tabular-nums">{{ myStanding.rank ? `#${myStanding.rank}` : '-' }}</dd>
        </div>
        <div class="min-w-28 px-4 py-2 text-right">
          <dt class="text-xs text-muted-foreground">{{ $t('competitions.label.teamPoints') }}</dt>
          <dd class="font-mono text-lg font-semibold tabular-nums text-primary">{{ myStanding.totalScore ?? 0 }} {{ $t('common.label.pts.scoreTrendChart') }}</dd>
        </div>
      </dl>
    </div>
    <Alert v-if="!isWriteUpReview && teamLoadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(teamLoadError) }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshMyTeam">{{ $t('common.label.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="!isWriteUpReview && standingError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(standingError) }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshMyStanding">{{ $t('common.label.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <NuxtPage />
  </div>

  <div v-else data-contained-workspace-page class="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <div v-else class="flex flex-col gap-6">
      <div class="flex flex-col gap-2">
        <div class="flex items-center gap-3">
          <Skeleton class="h-9 w-64" />
          <Skeleton class="h-6 w-14" />
          <Skeleton class="h-6 w-14" />
        </div>
        <Skeleton class="h-4 w-80" />
      </div>
      <Skeleton class="h-64 w-full" />
    </div>
  </div>
</template>
