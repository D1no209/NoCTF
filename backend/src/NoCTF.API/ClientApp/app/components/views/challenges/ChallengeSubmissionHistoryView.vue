<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeSubmissionHistoryViewState } from '~/features/challenges/useChallengeSubmissionHistory'

const viewProps = defineProps<{ state: ChallengeSubmissionHistoryViewState }>()
const { valueDialogOpen, valueSubmission, submittedValue, valueLoading, valueError, items, loading, error, hasMore, initialized, loadMore, loadNextPage, pendingPollingTimedOut, startPendingPolling, pendingPollingErrorMessage, resultVariant, resultText, canReadSubmittedValue, closeValueDialog, openSubmittedValue, setValueDialogOpen } = toRefs(viewProps.state)
</script>

<template>
  <section class="border-t pt-5" aria-labelledby="challenge-submission-history-title">
    <h3 id="challenge-submission-history-title" class="text-sm font-semibold">{{ $t('ui.challengeSubmissionHistory') }}</h3>

    <Alert v-if="error" variant="destructive" class="mt-3">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>
    <Alert v-else-if="pendingPollingTimedOut" variant="destructive" class="mt-3">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('ui.automaticSubmissionStatusUpdatesStoppedRetryManually') }}</span>
        <Button type="button" size="sm" variant="outline" @click="startPendingPolling">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="pendingPollingErrorMessage" variant="destructive" class="mt-3">
      <AlertDescription>{{ $t('ui.submissionStatusUpdateFailedAndWillRetryAutomatically', { reason: pendingPollingErrorMessage }) }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="mt-3 flex flex-col gap-2">
      <Skeleton v-for="index in 3" :key="index" class="h-11 w-full" />
    </div>

    <p v-else-if="initialized && !items.length" class="mt-3 text-sm text-muted-foreground">
      {{ $t('ui.noSubmissionsForThisChallengeYet') }}
    </p>

    <Table v-else class="mt-3">
      <TableHeader>
        <TableRow>
          <TableHead class="w-36">{{ $t('ui.type') }}</TableHead>
          <TableHead>{{ $t('ui.result') }}</TableHead>
          <TableHead class="w-44 text-right">{{ $t('ui.submissionTime') }}</TableHead>
          <TableHead class="w-28 text-right">{{ $t('ui.actions') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="submission in items" :key="submission.id">
          <TableCell><Badge variant="outline">{{ gameplayFactKindLabel(submission.kind) }}</Badge></TableCell>
          <TableCell>
            <Badge :variant="resultVariant(submission)" class="gap-1">
              <Spinner v-if="isGameplayFactPending(submission.state)" class="size-3" />
              {{ resultText(submission) }}
            </Badge>
          </TableCell>
          <TableCell class="text-right font-mono text-xs text-muted-foreground tabular-nums">
            {{ formatDateTime(submission.occurredAt) }}
          </TableCell>
          <TableCell class="text-right">
            <Button
              v-if="canReadSubmittedValue(submission)"
              variant="outline"
              size="sm"
              :disabled="valueLoading && valueSubmission?.id === submission.id"
              @click="openSubmittedValue(submission)"
            >
              <Spinner v-if="valueLoading && valueSubmission?.id === submission.id" data-icon="inline-start" />
              {{ $t('ui.viewFlag') }}
            </Button>
            <span v-else class="text-muted-foreground">-</span>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <Button v-if="hasMore" variant="outline" size="sm" class="mt-3" :disabled="loading" @click="loadNextPage">
      <Spinner v-if="loading" data-icon="inline-start" />{{ $t('ui.loadMore') }}
    </Button>

    <Dialog :open="valueDialogOpen" @update:open="setValueDialogOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('ui.submittedFlagValue') }}</DialogTitle>
          <DialogDescription>
            {{ $t('ui.onlyTeamMembersCanViewFlagValuesSubmittedByTheir') }}
          </DialogDescription>
        </DialogHeader>

        <div v-if="valueLoading" class="flex min-h-20 items-center justify-center">
          <Spinner class="size-5" />
        </div>
        <Alert v-else-if="valueError" variant="destructive">
          <AlertDescription>{{ $message(valueError) }}</AlertDescription>
        </Alert>
        <pre
          v-else-if="submittedValue"
          class="max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-md border bg-muted/40 p-4 font-mono text-sm select-text"
        >{{ submittedValue }}</pre>
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.noFlagValueIsAvailable') }}</p>

        <DialogFooter>
          <Button variant="outline" @click="closeValueDialog">{{ $t('ui.close') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </section>
</template>
