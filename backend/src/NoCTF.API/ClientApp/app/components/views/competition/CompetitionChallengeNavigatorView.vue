<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionChallengeNavigatorViewState } from '~/features/competition/useCompetitionChallengeNavigator'

const viewProps = defineProps<{ state: CompetitionChallengeNavigatorViewState }>()
const { ChevronRight, Flag, ShieldCheck, Swords, Users, bloodRankLabel, emit, isAwdp, items, loading, error, dataScope, hideSolved, board, progressFor, awdpProgressLabel, currentScore, visibleGroups, isDirectionCollapsed, toggleDirection, competitionId, selectedChallengeId } = toRefs(viewProps.state)
</script>

<template>
  <aside
    class="min-h-0 border-y bg-background/30 xl:sticky xl:top-20 xl:max-h-[calc(100svh-6rem)]"
    :aria-label="$t('ui.challengeList')"
  >
    <header class="border-b px-4 py-3">
      <h2 class="text-sm font-semibold">{{ $t('ui.challengeList') }}</h2>
      <p class="mt-1 text-xs text-muted-foreground">{{ $t('ui.chooseAChallengeByCategoryToViewItsDetailsIn') }}</p>
      <div class="mt-3 flex items-center justify-between gap-3 border-t pt-3">
        <Label :for="`hide-solved-${competitionId}`" class="cursor-pointer text-xs font-medium">
          {{ $t('ui.hideSolved') }}
        </Label>
        <Switch :id="`hide-solved-${competitionId}`" v-model="hideSolved" />
      </div>
    </header>

    <div v-if="loading" class="flex flex-col gap-2 p-3">
      <Skeleton v-for="index in 7" :key="index" class="h-12 w-full" />
    </div>

    <div v-else-if="error" class="p-3">
      <Alert variant="destructive">
        <AlertDescription>{{ $message(error) }}</AlertDescription>
      </Alert>
    </div>

    <Empty v-else-if="!items.length" class="border-0 py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.thereAreNoPublishedTopicsYet') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>

    <div v-else class="flex max-h-[calc(100svh-12rem)] flex-col gap-4 overflow-y-auto p-3">
      <Alert v-if="board.error.value" variant="destructive" class="text-xs">
        <AlertDescription>{{ $message(board.error.value) }}</AlertDescription>
      </Alert>
      <Alert v-else-if="board.processing.value" class="text-xs">
        <AlertDescription class="flex items-center gap-2">
          <Spinner class="size-3" /> {{ $t('ui.scoreboardDataIsBeingProjectedPleaseWait') }}
        </AlertDescription>
      </Alert>
      <Alert v-else-if="dataScope === 'Frozen'" class="text-xs">
        <AlertDescription>{{ $t('ui.theRankingListHasBeenFrozenAndTheQuestionScores') }}</AlertDescription>
      </Alert>

      <Empty v-if="hideSolved && !visibleGroups.length" class="border-0 py-8">
        <EmptyHeader><EmptyTitle>{{ $t('ui.noUnsolvedChallenges') }}</EmptyTitle></EmptyHeader>
      </Empty>

      <section v-for="(group, groupIndex) in visibleGroups" :key="group.direction" class="flex flex-col gap-2">
        <ActionButton
          type="button"
          class="flex w-full items-center gap-2 rounded-sm px-1 py-0.5 text-left text-xs font-semibold hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          :aria-expanded="!isDirectionCollapsed(group.direction)"
          :aria-controls="`challenge-direction-${competitionId}-${groupIndex}`"
          @click="toggleDirection(group.direction)"
        >
          <component
            :is="directionIcon(group.direction)"
            class="size-4"
            :class="directionTextClass(group.direction)"
            aria-hidden="true"
          />
          <span class="truncate">{{ group.direction }}</span>
          <span class="ml-auto font-mono text-[0.6875rem] tabular-nums text-muted-foreground">{{ group.challenges.length }}</span>
          <ChevronRight
            class="size-3.5 shrink-0 transition-transform"
            :class="!isDirectionCollapsed(group.direction) && 'rotate-90'"
            aria-hidden="true"
          />
        </ActionButton>

        <div
          :id="`challenge-direction-${competitionId}-${groupIndex}`"
          v-show="!isDirectionCollapsed(group.direction)"
          class="flex flex-col gap-2"
        >
          <ActionButton
            v-for="challenge in group.challenges"
            :key="challenge.id"
            type="button"
            class="group flex w-full items-center gap-2 rounded-md border px-3 py-2.5 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            :class="selectedChallengeId === challenge.id
              ? 'border-primary bg-primary/10 text-foreground'
              : 'border-border bg-background text-muted-foreground hover:border-primary/40 hover:text-foreground'"
            :aria-current="selectedChallengeId === challenge.id ? 'true' : undefined"
            @click="emit('select', challenge.id!)"
          >
            <span class="min-w-0 flex-1">
              <span class="flex min-w-0 items-baseline gap-2 text-sm font-medium">
                <span class="truncate">{{ challenge.title }}</span>
                <span v-if="currentScore(challenge.id) !== null" class="shrink-0 font-mono text-xs tabular-nums text-primary">
                  {{ currentScore(challenge.id) }} {{ $t('ui.pts2') }}
                </span>
              </span>
              <span v-if="isAwdp && progressFor(challenge.id)" class="mt-1 flex items-center gap-2 text-[0.6875rem]">
                <span class="flex items-center gap-1"><Swords class="size-3" /><span class="font-mono">{{ progressFor(challenge.id)?.attackCount ?? 0 }}</span></span>
                <span class="flex items-center gap-1"><ShieldCheck class="size-3" /><span class="font-mono">{{ progressFor(challenge.id)?.defenseCount ?? 0 }}</span></span>
                <span v-if="board.snapshot.value?.currentRoundId" class="truncate text-muted-foreground">{{ $t('ui.pendingRoundSettlement') }}</span>
              </span>
              <span v-else-if="progressFor(challenge.id)" class="mt-1 flex items-center gap-1 text-[0.6875rem]">
                <Users class="size-3" />
                <span>{{ $t('ui.solvedByTeams', { count: progressFor(challenge.id)?.solveCount ?? 0 }) }}</span>
              </span>
            </span>
            <Flag
              v-if="progressFor(challenge.id)?.solvedByMyTeam"
              class="size-4 shrink-0 text-primary"
              :aria-label="progressFor(challenge.id)?.bloodRank
                ? bloodRankLabel(progressFor(challenge.id)?.bloodRank)
                : $t('ui.solved')"
            />
            <component
              :is="progressFor(challenge.id)?.attackSucceeded ? Swords : ShieldCheck"
              v-else-if="isAwdp && awdpProgressLabel(progressFor(challenge.id))"
              class="size-4 shrink-0 text-primary"
              :aria-label="awdpProgressLabel(progressFor(challenge.id)) ?? undefined"
            />
            <ChevronRight class="size-4 shrink-0 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
          </ActionButton>
        </div>
      </section>
    </div>
  </aside>
</template>
