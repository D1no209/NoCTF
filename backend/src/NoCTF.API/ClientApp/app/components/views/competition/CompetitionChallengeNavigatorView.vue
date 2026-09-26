<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionChallengeNavigatorViewState } from '~/features/competition/useCompetitionChallengeNavigator'
const viewProps = defineProps<{ state: CompetitionChallengeNavigatorViewState }>()
const { ShieldCheck, Swords, Users, directionGlyph, isAwdp, loading, error, dataScope, hideSolved, search, board, progressFor, currentScore, bloodsFor, bloodTooltip, progressIcon, progressIconLabel, emptyLabel, groupOptions, listOptions, selectChallenge, competitionId, selectedChallengeId } = toRefs(viewProps.state)
</script>

<template>
  <ChoiceSidebar data-challenge-navigator :groups="groupOptions" :items="listOptions" :model-value="selectedChallengeId || null" :loading="loading" :label="$t('ui.challengeList')" :loading-label="$t('ui.challengeList')" :empty-label="emptyLabel" controls="challenge-workspace-detail" @update:model-value="selectChallenge">
    <template #header>
      <header data-challenge-navigator-header class="flex flex-col gap-3 pr-10">
        <div class="flex min-w-0 items-center gap-3">
          <h2 class="shrink-0 text-base font-semibold">{{ $t('ui.challengeList') }}</h2>
          <Input :id="`challenge-search-${competitionId}`" v-model="search" class="min-w-0 flex-1" :placeholder="$t('challengeNavigator.searchPlaceholder')" :aria-label="$t('challengeNavigator.searchPlaceholder')" />
        </div>
        <div class="flex items-center justify-between gap-3">
          <Label :for="`hide-solved-${competitionId}`" class="cursor-pointer text-xs font-medium">{{ $t('ui.hideSolved') }}</Label>
          <Switch :id="`hide-solved-${competitionId}`" v-model="hideSolved" />
        </div>

      </header>
    </template>
    <template #feedback>
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <Alert v-else-if="board.error.value" variant="destructive"><AlertDescription>{{ $message(board.error.value) }}</AlertDescription></Alert>
      <p v-else-if="board.processing.value" class="flex items-center gap-2 text-xs text-muted-foreground" role="status"><Spinner class="size-3" />{{ $t('ui.scoreboardDataIsBeingProjectedPleaseWait') }}</p>
      <p v-else-if="dataScope === 'Frozen'" class="text-xs text-muted-foreground" role="status">{{ $t('ui.theRankingListHasBeenFrozenAndTheQuestionScores') }}</p>
    </template>
    <template #group="{ group }">
      <span class="inline-flex items-center gap-2 font-semibold" :class="directionWatermarkClass(group.value)">
        <TechnicalIcon :name="directionGlyph(group.value)" />
        <span>{{ group.label }}</span>
      </span>
    </template>
    <template #item="{ item }">
      <span
        data-challenge-item
        class="relative block h-28 min-w-0 flex-1"
      >
        <span data-challenge-item-panel class="relative isolate flex h-28 min-w-0 flex-col px-3 py-2">
          <IconWatermark
            :name="directionGlyph(item.challenge.direction)"
            size="compact"
            class="noctf-motion-challenge-watermark"
            :class="directionWatermarkClass(item.challenge.direction)"
          />
          <span class="sr-only">{{ directionLabel(item.challenge.direction) }}</span>
          <span data-challenge-item-summary class="relative z-10 flex h-6 min-w-0 items-center gap-2 font-sans text-sm font-bold italic text-primary">
            <span class="min-w-0 flex-1 truncate">{{ item.challenge.title }}</span>
            <Badge v-if="item.challenge.locked" variant="outline" class="shrink-0 text-[0.625rem] not-italic">
              {{ $t('progression.locked') }}
            </Badge>
            <Badge v-else-if="item.challenge.interactionKind === 'PatchVerification'" variant="secondary" class="shrink-0 text-[0.625rem] not-italic">
              {{ $t('ui.patchVerification') }}
            </Badge>
            <StatusIcon :name="progressIcon(item.challenge.id)" :label="progressIconLabel(item.challenge.id)" />
            <span v-if="currentScore(item.challenge.id) !== null" data-challenge-score class="shrink-0 whitespace-nowrap text-right font-sans text-sm leading-none font-bold italic tabular-nums text-primary">
              {{ currentScore(item.challenge.id) }} <span class="text-[0.625rem]">{{ $t('ui.pts2') }}</span>
            </span>
          </span>

          <span data-challenge-item-details class="noctf-motion-challenge-details relative z-10 mt-2 flex min-h-0 flex-1 flex-col">
            <span v-if="item.challenge.locked" class="mt-1 text-[0.6875rem] text-muted-foreground">
              {{ $t('progression.prerequisiteProgress', { satisfied: item.challenge.prerequisitesSatisfied ?? 0, total: item.challenge.prerequisitesTotal ?? 0 }) }}
            </span>
            <span v-if="isAwdp && progressFor(item.challenge.id)" class="flex items-center gap-2 text-[0.6875rem]">
              <span class="flex items-center gap-1"><Swords class="size-3" /><span class="font-mono">{{ progressFor(item.challenge.id)?.attackCount ?? 0 }}</span></span>
              <span class="flex items-center gap-1"><ShieldCheck class="size-3" /><span class="font-mono">{{ progressFor(item.challenge.id)?.defenseCount ?? 0 }}</span></span>
              <span v-if="board.snapshot.value?.currentRoundId" class="truncate text-muted-foreground">{{ $t('ui.pendingRoundSettlement') }}</span>
            </span>
            <span v-else-if="progressFor(item.challenge.id)" class="flex items-center gap-1 text-[0.6875rem]">
              <Users class="size-3" />
              <span>{{ $t('ui.solvedByTeams', { count: progressFor(item.challenge.id)?.solveCount ?? 0 }) }}</span>
            </span>
            <span v-if="bloodsFor(item.challenge.id).length" class="mt-auto flex min-w-0 flex-wrap items-center gap-1.5 pt-3">
              <Hint v-for="blood in bloodsFor(item.challenge.id)" :key="blood.rank" :content="bloodTooltip(blood)">
                <BloodMark :rank="blood.rank" :label="bloodTooltip(blood)" :highlighted="blood.earnedByMyTeam" />
              </Hint>
            </span>
          </span>
        </span>
      </span>

    </template>
  </ChoiceSidebar>
</template>
