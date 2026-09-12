<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdWriteUpsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage'

const viewProps = defineProps<{ state: CompetitionsByIdWriteUpsPageViewState }>()
const { Download, FileSearch, MessageCircleQuestion, MinusCircle, RefreshCw, Scale, competitionId, review, loading, loadError, selectedTeamId, selected, canJudge, previewUrl, previewLoading, previewError, downloadPending, load, selectTeam, download, selectedChallengeId, adjustmentDelta, adjustmentPending, adjustmentError, submitAdjustment, clearAdjustmentError, selectChallenge, deduction, openDeduction, setDeductionOpen, confirmDeduction, consultationOpen, consultationChallengeId, consultationTitle, consultationBody, consultationPending, consultationError, openConsultation, submitConsultation, setConsultationOpen, clearConsultationError, CompetitionParticipantWorkspace } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId" :content-scroll="false">
    <Card class="flex h-[calc(100svh-9rem)] min-h-[42rem] flex-col overflow-hidden py-0">
      <CardHeader class="shrink-0 py-5">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-3">
            <FileSearch class="size-6 text-primary" />
            <CardTitle>{{ $t('writeUp.review') }}</CardTitle>
            <Badge variant="secondary">{{ $t('writeUp.submissionCount', { count: review?.items?.length ?? 0 }) }}</Badge>
          </div>
          <Button variant="outline" size="sm" :disabled="loading" @click="load">
            <Spinner v-if="loading" data-icon="inline-start" />
            <RefreshCw v-else data-icon="inline-start" />{{ $t('ui.refresh') }}
          </Button>
        </div>
      </CardHeader>

      <CardContent class="min-h-0 flex-1 pb-5">
        <Alert v-if="loadError" variant="destructive" class="mb-4">
          <AlertDescription>{{ $message(loadError) }}</AlertDescription>
        </Alert>
        <Skeleton v-if="loading && !review" class="h-full min-h-[32rem] w-full" />
        <Empty v-else-if="review?.items?.length === 0" class="h-full min-h-[32rem]">
          <EmptyHeader>
            <EmptyTitle>{{ $t('writeUp.noSubmissions') }}</EmptyTitle>
            <EmptyDescription>{{ $t('writeUp.noSubmissionsDescription') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>
        <div v-else class="grid h-full min-h-0 gap-4 lg:grid-cols-[15rem_minmax(0,1fr)] 2xl:grid-cols-[15rem_minmax(0,1fr)_20rem]">
          <section class="flex min-h-0 flex-col rounded-xl bg-muted/35 p-3 shadow-inner">
            <h2 class="mb-3 text-sm font-semibold">{{ $t('writeUp.teams') }}</h2>
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
                    {{ $t('writeUp.totalScore', { score: item.totalScore ?? 0 }) }}
                  </span>
                  <span class="text-xs text-muted-foreground">{{ formatDateTime(item.writeUp?.submittedAt) }}</span>
                </ActionButton>
              </div>
            </ScrollSurface>
          </section>

          <section class="flex min-h-0 min-w-0 flex-col gap-3">
            <div v-if="selected?.writeUp" class="flex flex-wrap items-start justify-between gap-3">
              <div class="min-w-0">
                <h2 class="truncate text-base font-semibold">{{ selected.writeUp.teamName }}</h2>
                <p class="truncate text-sm text-muted-foreground">
                  {{ selected.writeUp.fileName }} · {{ formatBytes(selected.writeUp.byteLength ?? 0) }}
                </p>
                <p class="truncate text-xs text-muted-foreground">
                  {{ $t('writeUp.reviewMetadata', { user: selected.writeUp.submittedByDisplayName ?? selected.writeUp.submittedByUserId ?? '-', time: formatDateTime(selected.writeUp.submittedAt) }) }}
                </p>
              </div>
              <Button variant="outline" size="sm" :disabled="downloadPending" @click="download">
                <Spinner v-if="downloadPending" data-icon="inline-start" />
                <Download v-else data-icon="inline-start" />{{ $t('ui.download') }}
              </Button>
            </div>
            <PdfPreview
              class="min-h-0 flex-1"
              :source="previewUrl"
              :loading="previewLoading"
              :error="previewError"
              :accessible-label="$t('writeUp.previewTitle', { team: selected?.writeUp?.teamName ?? '' })"
              :empty-label="$t('writeUp.previewEmpty')"
            />
          </section>

          <section class="flex min-h-0 flex-col rounded-xl bg-muted/35 p-4 shadow-inner lg:col-span-2 2xl:col-span-1">
            <div class="mb-3 flex items-center justify-between gap-3">
              <h2 class="flex items-center gap-2 text-sm font-semibold">
                <Scale class="size-4 text-primary" />{{ $t('writeUp.scoring') }}
              </h2>
              <Badge v-if="selected" variant="secondary" class="font-mono tabular-nums">
                {{ $t('writeUp.points', { score: selected.totalScore ?? 0 }) }}
              </Badge>
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
                    :disabled="(score.netPoints ?? 0) <= 0 || adjustmentPending"
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
                  <Button type="submit" :disabled="!selectedChallengeId || adjustmentDelta === 0 || adjustmentPending">
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
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <AlertDialogAction :disabled="adjustmentPending" @click="confirmDeduction">
            {{ $t('writeUp.deductToZero') }}
          </AlertDialogAction>
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
  </component>
</template>
