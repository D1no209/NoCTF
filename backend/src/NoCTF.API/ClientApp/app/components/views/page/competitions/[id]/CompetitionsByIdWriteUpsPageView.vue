<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdWriteUpsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage'

const viewProps = defineProps<{ state: CompetitionsByIdWriteUpsPageViewState }>()
const { ArrowLeft, Download, FileSearch, MessageCircleQuestion, MinusCircle, RefreshCw, Scale, competitionId, review, loading, loadError, selectedTeamId, selected, canJudge, previewUrl, previewLoading, previewError, downloadPending, load, selectTeam, download, selectedChallengeId, adjustmentDelta, adjustmentPending, adjustmentRefreshing, adjustmentBusy, adjustmentError, submitAdjustment, clearAdjustmentError, selectChallenge, deduction, openDeduction, setDeductionOpen, confirmDeduction, consultationOpen, consultationChallengeId, consultationTitle, consultationBody, consultationPending, consultationError, openConsultation, submitConsultation, setConsultationOpen, clearConsultationError } = toRefs(viewProps.state)
</script>

<template>
  <div data-writeup-review-workspace class="flex min-h-0 flex-1 flex-col">
    <Card slot-name="writeup-review-card" class="flex min-h-0 flex-1 flex-col overflow-hidden py-0">
      <CardContent class="min-h-0 flex-1 px-3 pb-3">
        <Alert v-if="loadError && !review" variant="destructive">
          <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
            <span>{{ $message(loadError) }}</span>
            <Button variant="outline" size="sm" @click="load">{{ $t('ui.retry') }}</Button>
          </AlertDescription>
        </Alert>
        <Skeleton v-else-if="loading && !review" class="h-full min-h-0 w-full" />
        <Empty v-else-if="review?.items?.length === 0" class="h-full min-h-0">
          <EmptyHeader>
            <EmptyTitle>{{ $t('writeUp.noSubmissions') }}</EmptyTitle>
            <EmptyDescription>{{ $t('writeUp.noSubmissionsDescription') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>
        <div v-else-if="review" data-scroll-surface data-scroll-axis="y" class="grid h-full min-h-0 gap-4 overflow-y-auto xl:grid-cols-[minmax(13rem,15rem)_minmax(0,1fr)_minmax(18rem,22rem)] xl:grid-rows-[minmax(0,1fr)] xl:overflow-hidden">
          <section class="flex min-h-0 flex-col rounded-xl bg-muted/35 p-3 shadow-inner">
            <header class="mb-3 flex items-center gap-2">
              <Button variant="ghost" size="icon-sm" as-child>
                <NuxtLink :to="`/competitions?competition=${competitionId}`" :aria-label="$t('ui.backToCompetition')">
                  <ArrowLeft />
                </NuxtLink>
              </Button>
              <FileSearch class="size-5 shrink-0 text-primary" />
              <h2 class="min-w-0 flex-1 truncate text-sm font-semibold">{{ $t('writeUp.review') }}</h2>
              <Badge variant="secondary">{{ $t('writeUp.submissionCount', { count: review.items?.length ?? 0 }) }}</Badge>
              <Button variant="ghost" size="icon-sm" :disabled="loading" :aria-label="$t('ui.refresh')" @click="load">
                <Spinner v-if="loading" />
                <RefreshCw v-else />
              </Button>
            </header>
            <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('writeUp.teams')">
              <div class="flex flex-col gap-1 pr-2">
                <ActionButton
                  v-for="item in review?.items ?? []"
                  :key="item.writeUp?.teamId"
                  class="flex w-full flex-col items-start gap-1 rounded-lg px-3 py-2 text-left"
                  :class="selectedTeamId === item.writeUp?.teamId ? 'bg-accent text-accent-foreground' : 'hover:bg-muted'"
                  @click="selectTeam(item.writeUp?.teamId)"
                >
                  <span class="w-full truncate font-semibold">{{ item.writeUp?.teamName }}</span>
                  <span class="font-mono text-xs tabular-nums text-muted-foreground">
                    {{ $t('writeUp.totalScore', { score: item.adjustedTotalScore ?? 0 }) }}
                  </span>
                  <span class="text-xs text-muted-foreground">{{ formatDateTime(item.writeUp?.submittedAt) }}</span>
                </ActionButton>
              </div>
            </ScrollSurface>
          </section>

          <section class="flex min-h-0 min-w-0 flex-col gap-3">
            <div v-if="selected?.writeUp" class="flex flex-wrap items-start gap-4">
              <div class="min-w-0 flex-1">
                <h2 class="truncate text-base font-semibold">{{ selected.writeUp.teamName }}</h2>
                <p class="truncate text-sm text-muted-foreground">
                  {{ selected.writeUp.fileName }} · {{ formatBytes(selected.writeUp.byteLength ?? 0) }}
                </p>
                <p class="truncate text-xs text-muted-foreground">
                  {{ $t('writeUp.reviewMetadata', { user: selected.writeUp.submittedByDisplayName ?? selected.writeUp.submittedByUserId ?? '-', time: formatDateTime(selected.writeUp.submittedAt) }) }}
                </p>
              </div>
              <dl class="grid shrink-0 grid-cols-2 gap-x-5 text-right">
                <div>
                  <dt class="text-[0.6875rem] text-muted-foreground">{{ $t('writeUp.originalRank') }}</dt>
                  <dd class="font-mono text-lg font-semibold tabular-nums">{{ selected.originalRank ? `#${selected.originalRank}` : '-' }}</dd>
                </div>
                <div>
                  <dt class="text-[0.6875rem] text-muted-foreground">{{ $t('writeUp.originalScore') }}</dt>
                  <dd class="font-mono text-lg font-semibold tabular-nums">{{ selected.originalTotalScore ?? '-' }} <span class="text-xs">{{ $t('ui.pts2') }}</span></dd>
                </div>
              </dl>
              <Button variant="outline" size="sm" :disabled="downloadPending" @click="download">
                <Spinner v-if="downloadPending" data-icon="inline-start" />
                <Download v-else data-icon="inline-start" />{{ $t('ui.download') }}
              </Button>
            </div>
            <PdfPreview
              fill
              class="min-h-0 flex-1"
              :source="previewUrl"
              :loading="previewLoading"
              :error="previewError"
              :accessible-label="$t('writeUp.previewTitle', { team: selected?.writeUp?.teamName ?? '' })"
              :empty-label="$t('writeUp.previewEmpty')"
            />
          </section>

          <section class="flex min-h-0 flex-col rounded-xl bg-muted/35 p-4 shadow-inner">
            <div class="mb-3 flex items-start justify-between gap-3">
              <h2 class="flex items-center gap-2 text-sm font-semibold">
                <Scale class="size-4 text-primary" />{{ $t('writeUp.scoring') }}
                <Badge v-if="adjustmentRefreshing" variant="secondary" class="gap-1.5">
                  <Spinner class="size-3" />{{ $t('writeUp.adjustmentRefreshing') }}
                </Badge>
              </h2>
              <dl v-if="selected" class="grid grid-cols-2 gap-x-4 text-right">
                <div>
                  <dt class="text-[0.6875rem] text-muted-foreground">{{ $t('writeUp.adjustedRank') }}</dt>
                  <dd class="font-mono text-xl font-bold tabular-nums text-primary">{{ selected.adjustedRank ? `#${selected.adjustedRank}` : '-' }}</dd>
                </div>
                <div>
                  <dt class="text-[0.6875rem] text-muted-foreground">{{ $t('writeUp.adjustedScore') }}</dt>
                  <dd class="font-mono text-xl font-bold tabular-nums text-primary">{{ selected.adjustedTotalScore ?? '-' }} <span class="text-xs">{{ $t('ui.pts2') }}</span></dd>
                </div>
              </dl>
            </div>
            <Alert v-if="review?.scoreboardAvailable === false" class="mb-3">
              <AlertDescription>{{ $t('writeUp.scoreboardUnavailable') }}</AlertDescription>
            </Alert>
            <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('writeUp.challengeScores')">
              <div class="flex flex-col gap-2 pr-2">
                <div
                  v-for="score in selected?.challengeScores ?? []"
                  :key="score.competitionChallengeId"
                  class="flex items-center justify-between gap-2 rounded-lg bg-background/35 px-3 py-2"
                >
                  <ActionButton
                    class="min-w-0 flex-1 text-left"
                    @click="selectChallenge(score)"
                  >
                    <span class="block truncate text-sm font-medium">{{ score.title }}</span>
                    <span class="font-mono text-xs tabular-nums text-muted-foreground">{{ $t('writeUp.points', { score: score.netPoints ?? 0 }) }}</span>
                  </ActionButton>
                  <Button
                    v-if="canJudge"
                    variant="ghost"
                    size="icon-sm"
                    :disabled="(score.netPoints ?? 0) <= 0 || adjustmentBusy"
                    :aria-label="$t('writeUp.deductChallengeScore', { challenge: score.title ?? '' })"
                    @click="openDeduction(score)"
                  >
                    <MinusCircle />
                  </Button>
                </div>
              </div>
            </ScrollSurface>

            <template v-if="canJudge && selected">
              <Separator class="my-4" />
              <UiForm validation="feature" class="flex flex-col gap-3" @submit.prevent="submitAdjustment">
                <Field>
                  <FieldLabel>{{ $t('writeUp.challenge') }}</FieldLabel>
                  <Select v-model="selectedChallengeId">
                    <SelectTrigger><SelectValue :placeholder="$t('writeUp.selectChallenge')" /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem
                          v-for="score in selected.challengeScores ?? []"
                          :key="score.competitionChallengeId"
                          :value="score.competitionChallengeId ?? ''"
                        >{{ score.title }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="writeup-adjustment">{{ $t('writeUp.adjustmentDelta') }}</FieldLabel>
                  <NumberInput id="writeup-adjustment" v-model="adjustmentDelta" :step="1" @update:model-value="clearAdjustmentError" />
                  <FieldDescription>{{ $t('writeUp.adjustmentDescription') }}</FieldDescription>
                  <FieldError v-if="adjustmentError">{{ $message(adjustmentError) }}</FieldError>
                </Field>
                <div class="flex flex-wrap gap-2">
                  <Button type="submit" :disabled="!selectedChallengeId || adjustmentDelta === 0 || adjustmentBusy">
                    <Spinner v-if="adjustmentPending" data-icon="inline-start" />{{ $t('writeUp.applyAdjustment') }}
                  </Button>
                  <Button type="button" variant="outline" @click="openConsultation">
                    <MessageCircleQuestion data-icon="inline-start" />{{ $t('writeUp.quickConsultation') }}
                  </Button>
                </div>
              </UiForm>
            </template>
            <p v-else-if="selected" class="mt-4 text-sm text-muted-foreground">{{ $t('writeUp.observerReadOnly') }}</p>
          </section>
        </div>
      </CardContent>
    </Card>

    <AlertDialog :open="deduction !== null" @update:open="setDeductionOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('writeUp.confirmDeductionTitle') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('writeUp.confirmDeductionDescription', { challenge: deduction?.title ?? '', score: deduction?.netPoints ?? 0 }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <Alert v-if="adjustmentError" variant="destructive">
          <AlertDescription>{{ $message(adjustmentError) }}</AlertDescription>
        </Alert>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="adjustmentPending">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button :disabled="adjustmentPending" @click="confirmDeduction">
            <Spinner v-if="adjustmentPending" data-icon="inline-start" />
            {{ $t('writeUp.deductToZero') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog :open="consultationOpen" @update:open="setConsultationOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('writeUp.quickConsultation') }}</DialogTitle>
          <DialogDescription>{{ $t('writeUp.consultationDescription', { team: selected?.writeUp?.teamName ?? '' }) }}</DialogDescription>
        </DialogHeader>
        <UiForm validation="feature" @submit.prevent="submitConsultation">
          <FieldGroup>
            <Field>
              <FieldLabel>{{ $t('writeUp.relatedChallenge') }}</FieldLabel>
              <Select v-model="consultationChallengeId">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="none">{{ $t('writeUp.wholeWriteUp') }}</SelectItem>
                    <SelectItem
                      v-for="score in selected?.challengeScores ?? []"
                      :key="score.competitionChallengeId"
                      :value="score.competitionChallengeId ?? ''"
                    >{{ score.title }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="writeup-consultation-title">{{ $t('ui.title') }}</FieldLabel>
              <Input id="writeup-consultation-title" v-model="consultationTitle" maxlength="160" required @input="clearConsultationError" />
            </Field>
            <Field>
              <FieldLabel for="writeup-consultation-body">{{ $t('ui.content') }}</FieldLabel>
              <Textarea id="writeup-consultation-body" v-model="consultationBody" rows="7" maxlength="4000" required @input="clearConsultationError" />
              <FieldError v-if="consultationError">{{ $message(consultationError) }}</FieldError>
            </Field>
          </FieldGroup>
        </UiForm>
        <DialogFooter>
          <Button variant="outline" :disabled="consultationPending" @click="setConsultationOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="consultationPending || !consultationTitle.trim() || !consultationBody.trim()" @click="submitConsultation">
            <Spinner v-if="consultationPending" data-icon="inline-start" />{{ $t('writeUp.startConsultation') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
