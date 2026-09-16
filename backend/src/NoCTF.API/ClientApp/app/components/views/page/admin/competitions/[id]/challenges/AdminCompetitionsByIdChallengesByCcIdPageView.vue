<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdChallengesByCcIdPageViewState } from '~/features/routes/admin/competitions/[id]/challenges/useAdminCompetitionsByIdChallengesByCcIdPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdChallengesByCcIdPageViewState }>()
const { Plus, competitionId, competition, canWrite, canJudge, challenge, loading, loadError, activeSection, sectionOptions, editCustomTitle, editOrder, editPublished, savingEdit, saveEdit, config, configLoading, savingConfig, inheritedConfigJson, saveConfig, hints, hintsLoading, hintsLoadError, includeDeletedHints, hintDialogOpen, editingHint, hintForm, hintError, savingHint, pendingHintId, openHintDialog, saveHint, deleteHint, restoreHint, scoringLoading, scoringError, loadChallengeTeamScoring, scoringDisplayNames, scoringRows, adjustmentTarget, adjustmentDelta, adjustmentPending, adjustmentError, adjustmentValid, openAdjustment, closeAdjustment, submitAdjustment, ChallengeRulesEditor, hiddenRuleKeys, onClickAdjustmentTarget, onClickHintDialogOpen } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <Button variant="ghost" size="sm" as-child class="w-fit">
      <NuxtLink :to="`/admin/competitions/${competitionId}/challenges`">{{ $t('ui.returnToQuestionList') }}</NuxtLink>
    </Button>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>
    <Skeleton v-else-if="loading" class="h-32 w-full" />

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-display text-xl">{{ challenge.title }}</h2>
        <Badge :variant="challenge.isPublished ? 'default' : 'outline'">
          {{ challenge.isPublished ? $t('ui.published') : $t('ui.unpublished') }}
        </Badge>
        <Badge variant="secondary">{{ directionLabel(challenge.direction) }}</Badge>
      </div>

      <div data-challenge-editor-workspace>
        <ChoiceSidebar
          v-model="activeSection"
          data-side="right"
          :items="sectionOptions"
          :label="$t('ui.questionConfiguration')"
          :loading-label="$t('ui.questionConfiguration')"
          :empty-label="$t('ui.questionConfiguration')"
          controls="competition-challenge-editor-content"
        />

        <div id="competition-challenge-editor-content" data-challenge-editor-content class="min-w-0">
        <section v-if="activeSection === 'general'">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('ui.basicSettings') }}</CardTitle>
            </CardHeader>
            <CardContent>
              <UiForm @submit.prevent="saveEdit">
                <FieldGroup>
                  <Field>
                    <FieldLabel for="cc-title">{{ $t('ui.competitionChallengeName') }}</FieldLabel>
                    <Input
                      id="cc-title"
                      v-model="editCustomTitle"
                      maxlength="160"
                      :readonly="!canWrite"
                      :placeholder="$t('ui.leaveBlankToUseTheQuestionBankTemplateTitle')"
                    />
                    <FieldDescription>{{ $t('ui.changesTheDisplayNameForThisCompetitionOnlyAndDoes') }}</FieldDescription>
                  </Field>
                  <Field>
                    <FieldLabel for="cc-order">{{ $t('ui.order') }}</FieldLabel>
                    <NumberInput id="cc-order" v-model.number="editOrder"  min="0" :readonly="!canWrite" />
                  </Field>
                  <Field orientation="horizontal">
                    <Switch id="cc-published" v-model="editPublished" :disabled="!canWrite" />
                    <FieldLabel for="cc-published" class="font-normal">{{ $t('ui.publishTheQuestionVisibleToPlayers') }}</FieldLabel>
                  </Field>
                  <Field v-if="canWrite">
                    <Button type="submit" :disabled="savingEdit">
                      <Spinner v-if="savingEdit" data-icon="inline-start" /> {{ $t('ui.saveSettings') }} </Button>
                  </Field>
                </FieldGroup>
              </UiForm>
            </CardContent>
          </Card>
        </section>

        <section v-else-if="activeSection === 'config'">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('ui.questionRules') }}</CardTitle>
              <CardDescription>{{ $t('ui.theModeSpecificRulesForThisQuestionInThisCompetition') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <component :is="ChallengeRulesEditor"
                :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
                :json="config?.json"
                :inherited-json="inheritedConfigJson"
                :readonly="!canWrite"
                :loading="configLoading"
                :saving="savingConfig"
                :hidden-keys="hiddenRuleKeys"
                @save="saveConfig"
              />
            </CardContent>
          </Card>
        </section>

        <section v-else-if="activeSection === 'hints'">
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <Checkbox id="show-deleted-hints" v-model="includeDeletedHints" />
                <Label for="show-deleted-hints" class="text-sm text-muted-foreground">{{ $t('ui.showDeleted') }}</Label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openHintDialog()">
                <Plus data-icon="inline-start" /> {{ $t('ui.addHint') }} </Button>
            </div>
            <Alert v-if="hintsLoadError" variant="destructive">
              <AlertDescription>{{ $message(hintsLoadError) }}</AlertDescription>
            </Alert>
            <Skeleton v-if="hintsLoading" class="h-32 w-full" />
            <Empty v-else-if="!hintsLoadError && hints.length === 0" class="border border-dashed py-12">
              <EmptyHeader>
                <EmptyTitle>{{ $t('ui.noPromptYet') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else-if="hints.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('ui.content') }}</TableHead>
                  <TableHead class="w-20">{{ $t('ui.pointsDeducted') }}</TableHead>
                  <TableHead class="w-44">{{ $t('ui.releaseTime') }}</TableHead>
                  <TableHead class="w-24">{{ $t('ui.status') }}</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">{{ $t('ui.actions') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="h in hints" :key="h.id" :class="{ 'opacity-60': h.deletedAt }">
                  <TableCell class="max-w-96 truncate">{{ h.content }}</TableCell>
                  <TableCell class="font-mono tabular-nums">{{ h.cost }}</TableCell>
                  <TableCell class="font-mono text-xs tabular-nums">{{ h.publishedAt ? adminFormatDateTime(h.publishedAt) : $t('ui.unpublished') }}</TableCell>
                  <TableCell>
                    <Badge v-if="h.deletedAt" variant="destructive">{{ $t('ui.deleted') }}</Badge>
                    <Badge v-else :variant="h.publishedAt ? 'default' : 'outline'">
                      {{ h.publishedAt ? $t('ui.published') : $t('ui.unpublished') }}
                    </Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!h.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="openHintDialog(h)">{{ $t('ui.edit') }}</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="deleteHint(h)">{{ $t('ui.delete') }}</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingHintId === h.id" @click="restoreHint(h)">{{ $t('ui.restore2') }}</Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </section>

        <section v-else-if="activeSection === 'scoring'">
          <div class="flex flex-col gap-4">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <h3 class="text-lg font-semibold">{{ $t('ui.challengeTeamScoring') }}</h3>
              <Button variant="outline" size="sm" :disabled="scoringLoading" @click="loadChallengeTeamScoring">
                <Spinner v-if="scoringLoading" data-icon="inline-start" />{{ $t('ui.refresh') }}
              </Button>
            </div>

            <Alert v-if="scoringError" variant="destructive">
              <AlertDescription>{{ $message(scoringError) }}</AlertDescription>
            </Alert>
            <Skeleton v-if="scoringLoading" class="h-48 w-full" />
            <Empty v-else-if="!scoringError && scoringRows.length === 0" class="border border-dashed py-12">
              <EmptyHeader><EmptyTitle>{{ $t('ui.thereIsNoRegisteredTeamYet') }}</EmptyTitle></EmptyHeader>
            </Empty>
            <Table v-else-if="scoringRows.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('ui.team') }}</TableHead>
                  <TableHead class="w-28">{{ $t('ui.registrationStatus') }}</TableHead>
                  <TableHead class="w-32">{{ $t('ui.completion') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('ui.challengeScore') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('ui.manualCorrection') }}</TableHead>
                  <TableHead v-if="canJudge" class="w-28 text-right">{{ $t('ui.actions') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="row in scoringRows" :key="row.team.id">
                  <TableCell>
                    <div class="flex items-center gap-2">
                      <span class="font-medium">{{ teamDisplayName(row.team, scoringDisplayNames) }}</span>
                      <Badge v-if="row.team.isBanned" variant="destructive">{{ $t('ui.banned2') }}</Badge>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge :variant="row.team.registrationStatus === 'Approved' ? 'default' : row.team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                      {{ enumLabel(TeamRegistrationStatusLabel, row.team.registrationStatus) }}
                    </Badge>
                  </TableCell>
                  <TableCell><Badge :variant="row.progressVariant">{{ row.progressLabel }}</Badge></TableCell>
                  <TableCell class="text-right font-mono font-semibold tabular-nums">{{ row.score }} {{ $t('ui.pts2') }}</TableCell>
                  <TableCell class="text-right font-mono tabular-nums" :class="row.adjustment < 0 ? 'text-destructive' : row.adjustment > 0 ? 'text-emerald-600' : 'text-muted-foreground'">
                    {{ row.adjustment > 0 ? '+' : '' }}{{ row.adjustment }} {{ $t('ui.pts2') }}
                  </TableCell>
                  <TableCell v-if="canJudge" class="text-right">
                    <Button variant="outline" size="sm" @click="openAdjustment(row)">{{ $t('ui.correctScore') }}</Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </section>
        </div>
      </div>

      <Dialog :open="adjustmentTarget !== null" @update:open="closeAdjustment">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ $t('ui.correctChallengeScore') }}</DialogTitle>
            <DialogDescription>
              {{ $t('ui.recordAPositiveOrNegativeScoreCorrectionForTeamOn', { team: adjustmentTarget ? teamDisplayName(adjustmentTarget.team, scoringDisplayNames) : '-' }) }}
            </DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="adjustmentError" variant="destructive"><AlertDescription>{{ $message(adjustmentError) }}</AlertDescription></Alert>
            <dl v-if="adjustmentTarget" class="grid grid-cols-2 gap-3 rounded-md border p-3 text-sm">
              <div>
                <dt class="text-muted-foreground">{{ $t('ui.currentChallengeScore') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score }} {{ $t('ui.pts2') }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ $t('ui.afterAdjustment') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score + adjustmentDelta }} {{ $t('ui.pts2') }}</dd>
              </div>
            </dl>
            <Field>
              <FieldLabel for="challenge-score-delta">{{ $t('ui.scoreDelta') }}</FieldLabel>
              <NumberInput id="challenge-score-delta" v-model.number="adjustmentDelta"  step="1" />
              <FieldDescription>{{ $t('ui.enterANonZeroIntegerSuchAs25Or10') }}</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" :disabled="adjustmentPending" @click="onClickAdjustmentTarget(null)">{{ $t('ui.cancel') }}</Button>
            <Button :disabled="adjustmentPending || !adjustmentValid" @click="submitAdjustment">
              <Spinner v-if="adjustmentPending" data-icon="inline-start" />{{ $t('ui.confirmAdjustment') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="hintDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingHint ? $t('ui.editingTips') : $t('ui.addHint') }}</DialogTitle>
            <DialogDescription>{{ $t('ui.leaveTheReleaseTimeBlankToIndicateThatItWill') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="hintError" variant="destructive">
              <AlertDescription>{{ $message(hintError) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="hint-content">{{ $t('ui.promptContent') }}</FieldLabel>
              <Textarea id="hint-content" v-model="hintForm.content" required />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="hint-cost">{{ $t('ui.pointsDeducted') }}</FieldLabel>
                <NumberInput id="hint-cost" v-model.number="hintForm.cost"  min="0" />
              </Field>
              <Field>
                <FieldLabel for="hint-publish">{{ $t('ui.releaseTimeOptional') }}</FieldLabel>
                <DateTimePicker id="hint-publish" v-model="hintForm.publishedAt"  />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="onClickHintDialogOpen(false)">{{ $t('ui.cancel') }}</Button>
            <Button :disabled="savingHint" @click="saveHint">
              <Spinner v-if="savingHint" data-icon="inline-start" /> {{ $t('ui.save') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
  </div>
</template>
