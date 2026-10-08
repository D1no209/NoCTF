<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdChallengesByCcIdPageViewState } from '~/features/routes/admin/competitions/[id]/challenges/useAdminCompetitionsByIdChallengesByCcIdPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdChallengesByCcIdPageViewState }>()
const { ccId, SingleWriteUpSettings, adminTeamPath, adminTemplatePath, Plus, competitionId, competition, canWrite, canJudge, challenge, loading, loadError, activeSection, sectionOptions, directions, directionLoading, directionError, editDirectionId, loadDirections, editCustomTitle, editTags, tagOptions, updateEditTags, editOrder, editPublished, savingEdit, saveEdit, config, configLoading, savingConfig, inheritedConfiguration, saveConfig, hints, hintsLoading, hintsLoadError, includeDeletedHints, hintDialogOpen, editingHint, hintForm, hintError, savingHint, pendingHintId, openHintDialog, saveHint, deleteHint, restoreHint, scoringLoading, scoringError, scoringSearch, loadChallengeTeamScoring, scoringPage, scoringPageCount, scoringTotal, scoringPageLimit, scoringPageLoading, loadScoringPage, setScoringPageSize, scoringDisplayNames, scoringRows, adjustmentTarget, adjustmentDelta, adjustmentPending, adjustmentError, adjustmentValid, openAdjustment, closeAdjustment, submitAdjustment, ChallengeRulesEditor, hiddenRuleKeys, onClickAdjustmentTarget, onClickHintDialogOpen } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-4">
    <Button variant="ghost" size="sm" as-child class="w-fit">
      <NuxtLink :to="`/admin/competitions/${competitionId}/challenges`">{{ $t('administration.label.returnQuestionList') }}</NuxtLink>
    </Button>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>
    <Skeleton v-else-if="loading" class="h-32 w-full" />

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-display text-xl">{{ challenge.title }}</h2>
        <Badge :variant="challenge.isPublished ? 'default' : 'outline'">
          {{ challenge.isPublished ? $t('administration.label.published') : $t('administration.label.unpublished') }}
        </Badge>
        <Badge variant="secondary"><span class="inline-flex items-center gap-1.5"><LucideIcon v-if="challenge.directionIcon" :name="challenge.directionIcon" class="size-3.5" />{{ challenge.direction }}</span></Badge>
        <NuxtLink v-if="challenge.challengeId" :to="adminTemplatePath(challenge.challengeId)" class="text-sm hover:underline">{{ $t('runtime.label.challengeTemplate') }}</NuxtLink>
      </div>

      <div data-challenge-editor-workspace>
        <ChoiceSidebar
          v-model="activeSection"
          data-side="right"
          :items="sectionOptions"
          :label="$t('administration.label.questionConfiguration')"
          :loading-label="$t('administration.label.questionConfiguration')"
          :empty-label="$t('administration.label.questionConfiguration')"
          controls="competition-challenge-editor-content"
        />

        <div id="competition-challenge-editor-content" data-challenge-editor-content class="min-w-0">
        <section v-if="activeSection === 'general'">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('administration.label.basicSettings') }}</CardTitle>
            </CardHeader>
            <CardContent>
              <UiForm @submit.prevent="saveEdit">
                <FieldGroup>
                  <Field>
                    <FieldLabel for="cc-title">{{ $t('administration.label.competitionChallengeName') }}</FieldLabel>
                    <Input
                      id="cc-title"
                      v-model="editCustomTitle"
                      maxlength="160"
                      :readonly="!canWrite"
                      :placeholder="$t('administration.competitionsBy.description.leaveBlankQuestionBank')"
                    />
                    <FieldDescription>{{ $t('administration.competitionsBy.description.changesDisplayNameCompetition') }}</FieldDescription>
                  </Field>
                  <Field>
                    <FieldLabel for="cc-direction">{{ $t('directionSettings.title') }}</FieldLabel>
                    <Select v-model="editDirectionId" :disabled="!canWrite || directionLoading || savingEdit || !!directionError">
                      <SelectTrigger id="cc-direction"><SelectValue :placeholder="$t('directionSettings.choose')" /></SelectTrigger>
                      <SelectContent><SelectGroup><SelectItem v-for="direction in directions" :key="direction.id" :value="direction.id || ''"><span class="inline-flex items-center gap-2"><LucideIcon :name="direction.icon || 'flag'" class="size-4" />{{ direction.name }}</span></SelectItem></SelectGroup></SelectContent>
                    </Select>
                    <Alert v-if="directionError" variant="destructive"><AlertDescription>{{ $message(directionError) }}<Button type="button" variant="ghost" size="sm" @click="loadDirections">{{ $t('common.label.refresh') }}</Button></AlertDescription></Alert>
                  </Field>
                  <Field>
                    <FieldLabel>{{ $t('challengeTags.label') }}</FieldLabel>
                    <TagPicker :model-value="editTags" :options="tagOptions" :label="$t('challengeTags.edit')" allow-create :disabled="!canWrite || savingEdit" @update:model-value="updateEditTags" />
                  </Field>
                  <Field>
                    <FieldLabel for="cc-order">{{ $t('administration.label.order') }}</FieldLabel>
                    <NumberInput id="cc-order" v-model.number="editOrder"  min="0" :readonly="!canWrite" />
                  </Field>
                  <Field orientation="horizontal">
                    <Switch id="cc-published" v-model="editPublished" :disabled="!canWrite" />
                    <FieldLabel for="cc-published" class="font-normal">{{ $t('administration.competitionsBy.description.publishQuestionVisiblePlayers') }}</FieldLabel>
                  </Field>
                  <Field v-if="canWrite">
                    <Button type="submit" :disabled="savingEdit || directionLoading || !!directionError">
                      <Spinner v-if="savingEdit" data-icon="inline-start" /> {{ $t('administration.label.saveSettings') }} </Button>
                  </Field>
                </FieldGroup>
              </UiForm>
            </CardContent>
          </Card>
        </section>

        <section v-else-if="activeSection === 'config'">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('administration.label.questionRules') }}</CardTitle>
              <CardDescription>{{ $t('administration.competitionsBy.description.modeSpecificRulesQuestion') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <component :is="ChallengeRulesEditor"
                :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
                :rules="config?.rules"
                :inherited-configuration="inheritedConfiguration"
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
                <Label for="show-deleted-hints" class="text-sm text-muted-foreground">{{ $t('administration.label.showDeleted') }}</Label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openHintDialog()">
                <Plus data-icon="inline-start" /> {{ $t('administration.label.addHint') }} </Button>
            </div>
            <Alert v-if="hintsLoadError" variant="destructive">
              <AlertDescription>{{ $message(hintsLoadError) }}</AlertDescription>
            </Alert>
            <Skeleton v-if="hintsLoading" class="h-32 w-full" />
            <Empty v-else-if="!hintsLoadError && hints.length === 0" class="border border-dashed py-12">
              <EmptyHeader>
                <EmptyTitle>{{ $t('administration.label.promptYet') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else-if="hints.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('common.label.content') }}</TableHead>
                  <TableHead class="w-20">{{ $t('administration.label.pointsDeducted') }}</TableHead>
                  <TableHead class="w-44">{{ $t('administration.label.releaseTime') }}</TableHead>
                  <TableHead class="w-24">{{ $t('common.label.status') }}</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">{{ $t('common.label.actions') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="h in hints" :key="h.id" :class="{ 'opacity-60': h.deletedAt }">
                  <TableCell class="max-w-96 truncate">{{ h.content }}</TableCell>
                  <TableCell class="font-mono tabular-nums">{{ h.cost }}</TableCell>
                  <TableCell class="font-mono text-xs tabular-nums">{{ h.publishedAt ? adminFormatDateTime(h.publishedAt) : $t('administration.label.unpublished') }}</TableCell>
                  <TableCell>
                    <Badge v-if="h.deletedAt" variant="destructive">{{ $t('common.label.deleted.competitionSidebarView') }}</Badge>
                    <Badge v-else :variant="h.publishedAt ? 'default' : 'outline'">
                      {{ h.publishedAt ? $t('administration.label.published') : $t('administration.label.unpublished') }}
                    </Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!h.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="openHintDialog(h)">{{ $t('administration.label.edit') }}</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="deleteHint(h)">{{ $t('common.action.delete') }}</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingHintId === h.id" @click="restoreHint(h)">{{ $t('administration.label.restore') }}</Button>
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
              <h3 class="text-lg font-semibold">{{ $t('administration.label.challengeTeamScoring') }}</h3>
              <Button variant="outline" size="sm" :disabled="scoringLoading" @click="loadChallengeTeamScoring">
                <Spinner v-if="scoringLoading" data-icon="inline-start" />{{ $t('common.label.refresh') }}
              </Button>
            </div>

            <Input
              v-model="scoringSearch"
              class="sm:max-w-sm"
              :placeholder="$t('common.label.searchTeam')"
              :aria-label="$t('common.label.searchTeam')"
            />

            <Alert v-if="scoringError" variant="destructive">
              <AlertDescription>{{ $message(scoringError) }}</AlertDescription>
            </Alert>
            <Skeleton v-if="scoringLoading" class="h-48 w-full" />
            <Empty v-else-if="!scoringError && scoringRows.length === 0" class="border border-dashed py-12">
              <EmptyHeader><EmptyTitle>{{ scoringSearch.trim() ? $t('administration.label.matchingTeams') : $t('administration.competitionsBy.description.thereRegisteredTeamYet') }}</EmptyTitle></EmptyHeader>
            </Empty>
            <Table v-else-if="scoringRows.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('common.label.team') }}</TableHead>
                  <TableHead class="w-28">{{ $t('administration.label.registrationStatus') }}</TableHead>
                  <TableHead class="w-32">{{ $t('administration.label.completion') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('administration.label.challengeScore') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('administration.label.manualCorrection') }}</TableHead>
                  <TableHead v-if="canJudge" class="w-28 text-right">{{ $t('common.label.actions') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="row in scoringRows" :key="row.team.id">
                  <TableCell>
                    <div class="flex items-center gap-2">
                      <NuxtLink :to="adminTeamPath(competitionId, row.team.id)" class="font-medium hover:underline">{{ teamDisplayName(row.team, scoringDisplayNames) }}</NuxtLink>
                      <Badge v-if="row.team.isBanned" variant="destructive">{{ $t('common.label.banned') }}</Badge>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge :variant="row.team.registrationStatus === 'Approved' ? 'default' : row.team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                      {{ enumLabel(TeamRegistrationStatusLabel, row.team.registrationStatus) }}
                    </Badge>
                  </TableCell>
                  <TableCell><Badge :variant="row.progressVariant">{{ row.progressLabel }}</Badge></TableCell>
                  <TableCell class="text-right font-mono font-semibold tabular-nums">{{ row.score }} {{ $t('common.label.pts.scoreTrendChart') }}</TableCell>
                  <TableCell class="text-right font-mono tabular-nums" :class="row.adjustment < 0 ? 'text-destructive' : row.adjustment > 0 ? 'text-emerald-600' : 'text-muted-foreground'">
                    {{ row.adjustment > 0 ? '+' : '' }}{{ row.adjustment }} {{ $t('common.label.pts.scoreTrendChart') }}
                  </TableCell>
                  <TableCell v-if="canJudge" class="text-right">
                    <Button variant="outline" size="sm" @click="openAdjustment(row)">{{ $t('administration.label.correctScore') }}</Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <OffsetPagination
              v-if="scoringRows.length > 0 || scoringTotal > 0"
              :page="scoringPage"
              :page-count="scoringPageCount"
              :total="scoringTotal"
              :limit="scoringPageLimit"
              :loading="scoringPageLoading"
              @update:page="loadScoringPage"
              @update:limit="setScoringPageSize"
            />
          </div>
        </section>
        </div>
      </div>

      <Dialog :open="adjustmentTarget !== null" @update:open="closeAdjustment">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ $t('administration.label.correctChallengeScore') }}</DialogTitle>
            <DialogDescription>
              {{ $t('administration.competitionsBy.description.recordPositiveNegativeScore', { team: adjustmentTarget ? teamDisplayName(adjustmentTarget.team, scoringDisplayNames) : '-' }) }}
            </DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="adjustmentError" variant="destructive"><AlertDescription>{{ $message(adjustmentError) }}</AlertDescription></Alert>
            <dl v-if="adjustmentTarget" class="grid grid-cols-2 gap-3 rounded-md border p-3 text-sm">
              <div>
                <dt class="text-muted-foreground">{{ $t('administration.label.challengeScore.idPageView') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score }} {{ $t('common.label.pts.scoreTrendChart') }}</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ $t('administration.label.adjustment') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score + adjustmentDelta }} {{ $t('common.label.pts.scoreTrendChart') }}</dd>
              </div>
            </dl>
            <Field>
              <FieldLabel for="challenge-score-delta">{{ $t('administration.label.scoreDelta') }}</FieldLabel>
              <NumberInput id="challenge-score-delta" v-model.number="adjustmentDelta"  step="1" />
              <FieldDescription>{{ $t('administration.competitionsBy.description.enterNonZeroInteger') }}</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" :disabled="adjustmentPending" @click="onClickAdjustmentTarget(null)">{{ $t('common.action.cancel') }}</Button>
            <Button :disabled="adjustmentPending || !adjustmentValid" @click="submitAdjustment">
              <Spinner v-if="adjustmentPending" data-icon="inline-start" />{{ $t('administration.label.confirmAdjustment') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="hintDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingHint ? $t('administration.label.editingTips') : $t('administration.label.addHint') }}</DialogTitle>
            <DialogDescription>{{ $t('administration.competitionsBy.description.leaveReleaseTimeBlank') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="hintError" variant="destructive">
              <AlertDescription>{{ $message(hintError) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="hint-content">{{ $t('administration.label.promptContent') }}</FieldLabel>
              <Textarea id="hint-content" v-model="hintForm.content" required />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="hint-cost">{{ $t('administration.label.pointsDeducted') }}</FieldLabel>
                <NumberInput id="hint-cost" v-model.number="hintForm.cost"  min="0" />
              </Field>
              <Field>
                <FieldLabel for="hint-publish">{{ $t('administration.label.releaseTimeOptional') }}</FieldLabel>
                <DateTimePicker id="hint-publish" v-model="hintForm.publishedAt"  />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="onClickHintDialogOpen(false)">{{ $t('common.action.cancel') }}</Button>
            <Button :disabled="savingHint" @click="saveHint">
              <Spinner v-if="savingHint" data-icon="inline-start" /> {{ $t('common.action.save') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
    <Card v-if="challenge"><CardContent><component :is="SingleWriteUpSettings" :competition-id="competitionId" :competition-challenge-id="ccId" :can-write="canWrite" /></CardContent></Card>
  </div>
</template>
