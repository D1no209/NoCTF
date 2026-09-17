<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdMySubmissionsPageViewState } from '~/features/routes/competitions/[id]/my/useCompetitionsByIdMySubmissionsPage'

const viewProps = defineProps<{ state: CompetitionsByIdMySubmissionsPageViewState }>()
const { competitionId, challengeTitles, challengeTitlesError, items, loading, error, hasMore, initialized, loadMore, loadChallengeTitles, timedOut, startPolling, pollingErrorMessage, resultVariant, resultText, CompetitionParticipantWorkspace } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId">
    <div class="flex flex-col gap-4">
    <h2 class="text-display text-lg">{{ $t('ui.mySubmissions') }}</h2>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>
    <Alert v-if="challengeTitlesError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(challengeTitlesError) }}</span>
        <Button type="button" size="sm" variant="outline" @click="loadChallengeTitles">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-if="timedOut" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('ui.automaticSubmissionStatusUpdatesStoppedRetryManually') }}</span>
        <Button type="button" size="sm" variant="outline" @click="startPolling">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="pollingErrorMessage" variant="destructive">
      <AlertDescription>{{ $t('ui.submissionStatusUpdateFailedAndWillRetryAutomatically', { reason: pollingErrorMessage }) }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 5" :key="i" class="h-12 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noSubmissionRecordYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.goToTheQuestionAreaToSolveTheProblemAnd') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else>
      <TableHeader>
        <TableRow>
          <TableHead>{{ $t('ui.challenge') }}</TableHead>
          <TableHead>{{ $t('ui.type') }}</TableHead>
          <TableHead>{{ $t('ui.status') }}</TableHead>
          <TableHead>{{ $t('ui.submissionTime') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="submission in items" :key="submission.id">
          <TableCell>
            <NuxtLink
              :to="`/competitions/${competitionId}/challenges?challenge=${submission.competitionChallengeId}`"
              prefetch-on="interaction"
              class="font-medium hover:underline"
            >
              {{ challengeTitles[submission.competitionChallengeId!] ?? $t('ui.unknownQuestion') }}
            </NuxtLink>
          </TableCell>
          <TableCell>
            <Badge variant="outline">{{ gameplayFactKindLabel(submission.kind) }}</Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="resultVariant(submission)" class="gap-1">
              <Spinner v-if="isGameplayFactPending(submission.state)" class="size-3" />
              {{ resultText(submission) }}
            </Badge>
          </TableCell>
          <TableCell class="font-mono text-xs text-muted-foreground tabular-nums">{{ formatDateTime(submission.occurredAt) }}</TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.loadMore') }} </Button>
    </div>
    </div>
  </component>
</template>
