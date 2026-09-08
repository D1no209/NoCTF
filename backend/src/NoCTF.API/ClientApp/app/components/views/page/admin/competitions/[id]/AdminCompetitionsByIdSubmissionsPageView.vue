<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdSubmissionsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdSubmissionsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdSubmissionsPageViewState }>()
const { Download, canWrite, canJudge, isAdministrator, canDownloadPatch, patchDownloading, patchErrors, downloadPatch, gameplayFactKindOptions, gameplayFactStateOptions, gameplayFactResultOptions, challengeOptions, teamOptions, teamName, challengeTitle, filterChallenge, filterTeam, filterKind, filterState, filterResult, filterFlag, previewItems, previewCursor, previewLoading, previewError, previewInitialized, differenceLabels, bloodRankLabel, loadPreview, items, loading, listError, hasMore, loadMore, initialized, applyFilters, detail, detailOpen, detailLoading, detailError, openDetail, actionPending, rejudgeOne, batchTarget, rejudgeBatch, queueEvaluation, flagDialog, flagResult, flagError, flagPending, openFlagAccess, closeFlagAccess, accessFlag, onClickFilterChallenge, onUpdateOpenChange } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardHeader class="flex flex-row items-start justify-between gap-4">
        <div class="space-y-1">
          <CardTitle>{{ $t('ui.historicalAdjudicationDifferencePreview') }}</CardTitle>
          <CardDescription>{{ $t('ui.thisPreviewAnalyzesCtfFlagFactsAndAwdpBreakFacts') }}</CardDescription>
        </div>
        <Button variant="outline" size="sm" :disabled="previewLoading" @click="loadPreview(true)">
          <Spinner v-if="previewLoading" data-icon="inline-start" /> {{ $t('ui.analyzeAgain') }}
        </Button>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <Alert v-if="previewError" variant="destructive">
          <AlertDescription>{{ $message(previewError) }}</AlertDescription>
        </Alert>
        <Skeleton v-else-if="previewLoading && !previewInitialized" class="h-24 w-full" />
        <Alert v-else-if="previewInitialized && previewItems.length === 0">
          <AlertDescription>{{ $t('ui.noAdjudicationOrBloodAwardDifferencesWereFoundInThe') }}</AlertDescription>
        </Alert>
        <div v-for="item in previewItems" :key="item.gameplayFactId" class="rounded-lg border p-4">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div>
              <p class="font-medium">{{ item.challengeTitle }} · {{ item.teamName ?? '-' }}</p>
              <p class="mt-1 font-mono text-xs text-muted-foreground">{{ item.gameplayFactId }} · {{ adminFormatDateTime(item.occurredAt) }}</p>
            </div>
            <div class="flex flex-wrap gap-1">
              <Badge v-for="difference in item.differences" :key="`${difference.kind}-${difference.certainty}`" :variant="difference.certainty === 'Deterministic' ? 'destructive' : 'secondary'">
                {{ difference.certainty === 'Deterministic' ? $t('ui.deterministicDifference') : $t('ui.needsManualReview') }}
              </Badge>
            </div>
          </div>
          <div class="mt-3 grid gap-2 text-sm sm:grid-cols-3">
            <p><span class="text-muted-foreground">{{ $t('ui.currentResult') }}：</span>{{ item.currentResult ? enumLabel(GameplayFactResultLabel, item.currentResult) : '-' }}</p>
            <p><span class="text-muted-foreground">{{ $t('ui.deterministicExpectation') }}：</span>{{ item.deterministicExpectedResult ? enumLabel(GameplayFactResultLabel, item.deterministicExpectedResult) : '-' }}</p>
            <p><span class="text-muted-foreground">{{ $t('ui.recordedBloodAwards') }}：</span>{{ item.recordedBloodRanks?.map(bloodRankLabel).join('、') || '-' }}</p>
          </div>
          <ul class="mt-3 list-disc space-y-1 pl-5 text-sm">
            <li v-for="difference in item.differences" :key="difference.kind">
              {{ $t(differenceLabels[difference.kind!]) }}
            </li>
          </ul>
        </div>
        <Button v-if="previewCursor" variant="outline" :disabled="previewLoading" @click="loadPreview(false)">
          <Spinner v-if="previewLoading" data-icon="inline-start" /> {{ $t('ui.continueScanningOlderRecords') }}
        </Button>
      </CardContent>
    </Card>

    <Card>
      <CardContent class="flex flex-col gap-3 pt-6">
        <div class="grid gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <Select v-model="filterChallenge">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('ui.topicsAll')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterTeam">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('ui.teamAll')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="t in teamOptions" :key="t.id" :value="t.id">{{ t.name }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterKind">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('ui.typeAll')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactKindOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterState">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('ui.reviewStatusAll')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactStateOptions" :key="option.value" :value="option.value">
                  {{ $t(option.label) }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Select v-model="filterResult">
            <SelectTrigger class="w-full">
              <SelectValue :placeholder="$t('ui.resultsAll')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="option in gameplayFactResultOptions" :key="option.value" :value="option.value">
                  {{ $t(option.label) }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Input v-model="filterFlag" :placeholder="$t('ui.theSubmittedFlagContains')" />
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <Button size="sm" @click="applyFilters">{{ $t('ui.applyFilters') }}</Button>
          <Button
            variant="ghost"
            size="sm"
            @click="onClickFilterChallenge"
          > {{ $t('ui.clear') }} </Button>
          <template v-if="canWrite">
            <Separator orientation="vertical" class="h-6" />
            <Select v-model="batchTarget">
              <SelectTrigger class="w-48">
                <SelectValue :placeholder="$t('ui.selectATopicBatchOperation')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="c in challengeOptions" :key="c.id" :value="c.id">{{ c.title }}</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="rejudgeBatch">
              <Spinner v-if="actionPending === 'batch'" data-icon="inline-start" /> {{ $t('ui.rejudgeTheEntireQuestion') }} </Button>
            <Button variant="outline" size="sm" :disabled="!batchTarget || actionPending !== null" @click="queueEvaluation">
              <Spinner v-if="actionPending === 'queue'" data-icon="inline-start" /> {{ $t('ui.triggerReview') }} </Button>
          </template>
        </div>
      </CardContent>
    </Card>

    <Alert v-if="listError" variant="destructive">
      <AlertDescription>{{ $message(listError.message) }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noMatchingSubmissions') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else-if="items.length > 0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('ui.team') }}</TableHead>
            <TableHead>{{ $t('ui.challenge') }}</TableHead>
            <TableHead class="w-20">{{ $t('ui.type') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.reviewStatus') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.result') }}</TableHead>
            <TableHead class="w-44">{{ $t('ui.submissionTime') }}</TableHead>
            <TableHead class="w-52 text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="s in items" :key="s.id">
            <TableCell class="font-medium">{{ teamName(s.teamId) }}</TableCell>
            <TableCell>{{ challengeTitle(s.competitionChallengeId) }}</TableCell>
            <TableCell>{{ enumLabel(GameplayFactKindLabel, s.kind) }}</TableCell>
            <TableCell>
              <Badge :variant="s.state === 'Completed' ? 'default' : s.state === 'PlatformFailed' ? 'destructive' : 'secondary'">
                {{ enumLabel(GameplayFactStateLabel, s.state) }}
              </Badge>
            </TableCell>
            <TableCell>
              <div v-if="s.result !== null && s.result !== undefined || s.failureCode" class="flex flex-col items-start gap-1">
                <Badge v-if="s.result !== null && s.result !== undefined" :variant="s.result === 'Correct' ? 'default' : 'outline'">
                  {{ enumLabel(GameplayFactResultLabel, s.result) }}
                </Badge>
                <span v-if="s.failureCode" class="text-xs text-muted-foreground">
                  {{ gameplayFactFailureCodeLabel(s.failureCode) }}
                </span>
              </div>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(s.occurredAt) }}</TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="openDetail(s.id)">{{ $t('ui.details') }}</Button>
                <Button v-if="canJudge && (s.kind === 'FlagAttempt' || s.kind === 'BreakAttempt')" variant="ghost" size="sm" @click="openFlagAccess(s.id)">{{ $t('ui.readFlag') }}</Button>
                <Button v-if="canDownloadPatch && s.kind === 'FixAttempt'" variant="ghost" size="sm" :disabled="patchDownloading.has(s.id ?? '')" @click="downloadPatch(s.id)">
                  <Spinner v-if="patchDownloading.has(s.id ?? '')" data-icon="inline-start" />
                  <Download v-else data-icon="inline-start" />{{ $t('ui.downloadPatch') }}
                </Button>
                <Button v-if="canWrite" variant="ghost" size="sm" :disabled="actionPending === s.id" @click="rejudgeOne(s.id)">
                  <Spinner v-if="actionPending === s.id" data-icon="inline-start" /> {{ $t('ui.heavySentence') }} </Button>
              </div>
              <Alert v-if="patchErrors[s.id ?? '']" variant="destructive" class="mt-2 text-left">
                <AlertDescription>{{ patchErrors[s.id ?? ''] }}</AlertDescription>
              </Alert>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMore">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.loadMore') }} </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('ui.submitDetails') }}</SheetTitle>
          <SheetDescription>{{ $t('ui.submissionId', { id: detail?.gameplayFactId ?? '-' }) }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <Alert v-else-if="detailError" variant="destructive" class="mx-4"><AlertDescription>{{ $message(detailError) }}</AlertDescription></Alert>
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.team') }}</span><span>{{ teamName(detail.teamId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.challenge') }}</span><span>{{ challengeTitle(detail.competitionChallengeId) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.perpetrator') }}</span><span class="font-mono text-xs">{{ detail.actorUserId ?? '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.type') }}</span><span>{{ enumLabel(GameplayFactKindLabel, detail.kind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.reviewStatus') }}</span><span>{{ enumLabel(GameplayFactStateLabel, detail.state) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.result') }}</span><span>{{ detail.result !== null && detail.result !== undefined ? enumLabel(GameplayFactResultLabel, detail.result) : '-' }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.failureReason') }}</span><span>{{ gameplayFactFailureCodeLabel(detail.failureCode) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.submissionTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.occurredAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">{{ $t('ui.updateTime') }}</span><span class="font-mono tabular-nums">{{ adminFormatDateTime(detail.updatedAt) }}</span></div>
          <section v-if="detail.kind === 'FixAttempt' && canDownloadPatch && detail.canDownloadPatch" class="flex flex-col gap-3">
            <Separator />
            <h3 class="font-semibold">{{ $t('ui.patchArchive') }}</h3>
            <p class="text-muted-foreground">{{ $t('ui.downloadsTheOriginalUploadWithoutOnlinePreviewExtractionOrExecution') }}</p>
            <Alert v-if="detail.patchFailure" variant="destructive"><AlertDescription>{{ adminPatchFailureMessage(detail.patchFailure) }}</AlertDescription></Alert>
            <dl v-if="detail.patch" class="flex flex-col gap-2">
              <dt class="text-muted-foreground">{{ $t('ui.originalFileName') }}</dt><dd class="break-all font-mono">{{ detail.patch.fileName }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.fileSize') }}</dt><dd>{{ formatBytes(detail.patch.byteLength) }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.uploadTime') }}</dt><dd class="font-mono">{{ adminFormatDateTime(detail.patch.uploadedAt) }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.sha256') }}</dt><dd class="break-all font-mono text-xs">{{ detail.patch.sha256 }}</dd>
            </dl>
            <Button variant="outline" :disabled="patchDownloading.has(detail.gameplayFactId ?? '')" @click="downloadPatch(detail.gameplayFactId)">
              <Spinner v-if="patchDownloading.has(detail.gameplayFactId ?? '')" data-icon="inline-start" />
              <Download v-else data-icon="inline-start" />{{ $t('ui.downloadPatch') }}
            </Button>
            <Alert v-if="patchErrors[detail.gameplayFactId ?? '']" variant="destructive"><AlertDescription>{{ patchErrors[detail.gameplayFactId ?? ''] }}</AlertDescription></Alert>
          </section>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="flagDialog !== null" @update:open="onUpdateOpenChange">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.readProtectedCommitFlag') }}</DialogTitle>
          <DialogDescription>
            {{ isAdministrator
              ? $t('ui.platformAdministratorFlagAccessIsNotWrittenToTheAudit')
              : $t('ui.competitionStaffFlagAccessIsWrittenToTheAuditLog') }}
          </DialogDescription>
        </DialogHeader>
        <div v-if="flagPending" class="flex min-h-24 items-center justify-center">
          <Spinner class="size-5" />
        </div>
        <Alert v-else-if="flagError" variant="destructive">
          <AlertTitle>{{ $t('ui.failedToReadFlag') }}</AlertTitle>
          <AlertDescription>{{ $message(flagError) }}</AlertDescription>
        </Alert>
        <template v-else-if="flagResult !== null">
          <div class="rounded-md border bg-muted p-3 font-mono text-sm break-all">{{ flagResult }}</div>
        </template>
        <DialogFooter>
          <Button v-if="flagError" variant="outline" :disabled="flagPending" @click="() => accessFlag()">
            {{ $t('ui.retry') }}
          </Button>
          <Button @click="closeFlagAccess">{{ $t('ui.close') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
