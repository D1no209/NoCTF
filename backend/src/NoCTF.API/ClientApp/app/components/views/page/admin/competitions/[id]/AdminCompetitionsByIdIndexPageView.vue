<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdIndexPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdIndexPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdIndexPageViewState }>()
const { startGateErrorMessage, competitionId, competition, canWrite, canManagePermissions, isAdministrator, pendingAction, actionError, status, isDeleted, steps, actions, confirmTarget, execute, trigger, validating, validationErrors, validateStart, generating, generateFailures, generateMissingFlags, deleting, restoring, hardDeleting, forceDeleting, deleteConfirm, forceDeleteOpen, forceDeleteTitle, forceDeleteReason, forceDeleteError, forceDeleteConflictingIds, hardDeletePreview, hardDeletePreviewLoading, hardDeletePreviewError, hardDeleteReferenceLabel, restore, forceDeleteValid, beginForceDelete, forceDelete, submitDelete, onClickDeleteConfirm, onClickDeleteConfirm2, onUpdateOpenConfirmTarget, onClickForceDeleteOpen, onUpdateOpenDeleteConfirm } = toRefs(viewProps.state)
</script>

<template>
  <div v-if="competition" class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.lifeCycle') }}</CardTitle>
        <CardDescription class="font-mono tabular-nums">
          {{ adminFormatDateTime(competition.startTime) }} ~ {{ adminFormatDateTime(competition.endTime) }}
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-6">
        <div class="flex flex-wrap items-center gap-2">
          <template v-for="(step, i) in steps" :key="step.value">
            <Badge :variant="status === step.value || (step.value === 'Running' && status === 'Paused') ? 'default' : 'outline'">
              {{ step.value === 'Running' && status === 'Paused' ? $t('ui.suspended') : $t(step.label) }}
            </Badge>
            <span v-if="i < steps.length - 1" class="text-muted-foreground">→</span>
          </template>
        </div>

        <Alert v-if="actionError" variant="destructive">
          <AlertDescription>{{ $message(actionError) }}</AlertDescription>
        </Alert>

        <div v-if="canWrite && !isDeleted" class="flex flex-wrap items-center gap-2">
          <Button v-if="status === 'Published'" variant="outline" size="sm" :disabled="validating" @click="validateStart">
            <Spinner v-if="validating" data-icon="inline-start" /> {{ $t('ui.checkBeforeStart') }} </Button>
          <Button
            v-for="a in actions.filter(a => a.visible)"
            :key="a.key"
            size="sm"
            :variant="a.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="trigger(a)"
          >
            <Spinner v-if="pendingAction === a.key" data-icon="inline-start" />
            {{ a.label }}
          </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.theCurrentRoleIsReadOnlyAndCannotPerformLife') }}</p>

        <div v-if="validationErrors && validationErrors.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(ve, i) in validationErrors" :key="i" variant="destructive">
            <AlertDescription>
              {{ startGateErrorMessage(ve) }}
              <NuxtLink
                v-if="ve.competitionChallengeId"
                class="underline"
                :to="`/admin/competitions/${competitionId}/challenges/${ve.competitionChallengeId}`"
              > {{ $t('ui.viewQuestions') }} </NuxtLink>
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite && !isDeleted">
      <CardHeader>
        <CardTitle>{{ $t('ui.flagGeneration') }}</CardTitle>
        <CardDescription>{{ $t('ui.generateMissingTeamFlagsInBatchesForDynamicFlagQuestions') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <div>
          <Button variant="outline" size="sm" :disabled="generating" @click="generateMissingFlags">
            <Spinner v-if="generating" data-icon="inline-start" /> {{ $t('ui.generateMissingFlag') }} </Button>
        </div>
        <div v-if="generateFailures && generateFailures.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(f, i) in generateFailures" :key="i" variant="destructive">
            <AlertDescription>
              {{ $t('ui.challengeTeam', { challenge: f.competitionChallengeId ?? '-', team: f.teamId ?? '-', description: f.description ?? f.code ?? '-' }) }}
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
    </Card>

    <Card v-if="canWrite">
      <CardHeader>
        <CardTitle class="text-destructive">{{ $t('ui.dangerZone') }}</CardTitle>
        <CardDescription v-if="isDeleted">
          {{ $t('ui.thisCompetitionWasSoftDeletedAtRestoringItWillNot', { time: adminFormatDateTime(competition.deletedAt) }) }}
        </CardDescription>
        <CardDescription v-else>{{ $t('ui.softDeletionIsReversiblePhysicalDeletionIsASeparateOperation') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col items-start gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <Button v-if="!isDeleted" variant="outline" size="sm" :disabled="deleting" @click="onClickDeleteConfirm('soft')"> {{ $t('ui.softDeleteCompetition') }} </Button>
          <template v-if="isDeleted && canManagePermissions">
            <Button variant="outline" size="sm" :disabled="restoring" @click="restore">
              <Spinner v-if="restoring" data-icon="inline-start" /> {{ $t('ui.restoreDeletedContest') }} </Button>
          </template>
          <Button
            v-if="canManagePermissions && hardDeletePreview?.canHardDelete"
            variant="destructive"
            size="sm"
            :disabled="hardDeleting"
            @click="onClickDeleteConfirm2('hard')"
          > {{ $t('ui.deleteCompletely') }} </Button>
          <Button
            v-if="isAdministrator"
            variant="destructive"
            size="sm"
            :disabled="forceDeleting"
            @click="beginForceDelete"
          > {{ $t('ui.forceCascadeDelete') }} </Button>
        </div>

        <div v-if="canManagePermissions && hardDeletePreviewLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
          <Spinner data-icon="inline-start" /> {{ $t('ui.checkingPermanentDeletionImpact') }}
        </div>
        <Alert v-else-if="canManagePermissions && hardDeletePreviewError" variant="destructive" class="w-full">
          <AlertDescription>{{ $message(hardDeletePreviewError) }}</AlertDescription>
        </Alert>
        <Alert v-else-if="canManagePermissions && hardDeletePreview && !hardDeletePreview.canHardDelete" class="w-full">
          <AlertTitle>{{ $t('ui.permanentDeletionIsProtected') }}</AlertTitle>
          <AlertDescription class="flex flex-col gap-2">
            <span>{{ $t('ui.theFollowingPermanentHistoryOrBusinessReferencesStillExistSo') }}</span>
            <span v-if="hardDeletePreview.references?.some(reference => reference.code === 'HistoricalEvent')" class="font-medium">
              {{ $t('ui.competitionEventsAreRetainedPermanentlyAndCannotBeRemovedOr') }}
            </span>
            <span class="flex flex-wrap gap-2">
              <Badge v-for="reference in hardDeletePreview.references" :key="reference.code" variant="outline">
                {{ hardDeleteReferenceLabel(reference.code) }} · {{ reference.count ?? 0 }}
              </Badge>
            </span>
            <span v-if="isAdministrator" class="text-destructive">
              {{ hardDeletePreview.canForceDelete
                ? $t('ui.platformAdministratorsCanForceACascadeDeletionThisPermanentlyRemoves')
                : hardDeletePreview.references?.some(reference => reference.code === 'NotificationScopeConflict')
                  ? $t('ui.forceDeletionBlockedNotificationThreadsContainCrossScopeOrUnproven')
                  : $t('ui.forceCascadeDeletionIsCurrentlyBlockedFinishTheCompetitionAnd') }}
            </span>
          </AlertDescription>
        </Alert>
        <Alert v-else-if="canManagePermissions && hardDeletePreview?.canHardDelete" class="w-full">
          <AlertDescription>{{ $t('ui.impactCheckPassedThisCompetitionHasNoPermanentHistoryOr') }}</AlertDescription>
        </Alert>
      </CardContent>
    </Card>

    <AlertDialog :open="confirmTarget !== null" @update:open="onUpdateOpenConfirmTarget">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTarget?.confirm?.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmTarget?.confirm?.description }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="pendingAction !== null">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            :variant="confirmTarget?.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="confirmTarget && execute(confirmTarget)"
          >
            <Spinner v-if="pendingAction !== null" data-icon="inline-start" />
            {{ pendingAction !== null ? $t('ui.processing') : $t('ui.confirm') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="forceDeleteOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('ui.forceDeleteCompetition') }}</DialogTitle>
          <DialogDescription>
            {{ $t('ui.thisCannotBeUndoneItPermanentlyDeletesTheCompetitionAnd') }}
          </DialogDescription>
        </DialogHeader>
        <Alert v-if="forceDeleteError" variant="destructive">
          <AlertDescription>
            {{ $message(forceDeleteError) }}
            <span v-if="forceDeleteConflictingIds.length" class="mt-2 block break-all font-mono text-xs">
              {{ $t('ui.conflictingNotificationIds') }}：{{ forceDeleteConflictingIds.join(', ') }}
            </span>
          </AlertDescription>
        </Alert>
        <FieldGroup>
          <Field>
            <FieldLabel for="force-delete-title">{{ $t('ui.enterTheFullCompetitionTitleToConfirm') }}</FieldLabel>
            <Input
              id="force-delete-title"
              v-model="forceDeleteTitle"
              autocomplete="off"
              :placeholder="competition.title"
              :disabled="forceDeleting"
            />
            <FieldError v-if="forceDeleteTitle && forceDeleteTitle !== competition.title">
              {{ $t('ui.theCompetitionTitleMustMatchExactly') }}
            </FieldError>
          </Field>
          <Field>
            <FieldLabel for="force-delete-reason">{{ $t('ui.reasonForDeletion') }}</FieldLabel>
            <Textarea
              id="force-delete-reason"
              v-model="forceDeleteReason"
              rows="4"
              maxlength="500"
              :disabled="forceDeleting"
            />
            <FieldDescription>{{ $t('ui.atLeast8CharactersThisReasonIsRecordedInThe') }}</FieldDescription>
            <FieldError v-if="forceDeleteReason && forceDeleteReason.trim().length < 8">
              {{ $t('ui.theDeletionReasonMustContainAtLeast8Characters') }}
            </FieldError>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="forceDeleting" @click="onClickForceDeleteOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button variant="destructive" :disabled="forceDeleting || !forceDeleteValid" @click="forceDelete">
            <Spinner v-if="forceDeleting" data-icon="inline-start" />
            {{ forceDeleting ? $t('ui.permanentlyDeleting') : $t('ui.confirmForceDeletion') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteConfirm !== null" @update:open="onUpdateOpenDeleteConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ deleteConfirm === 'hard' ? $t('ui.completelyDeleteTheContest') : $t('ui.deleteContest') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ deleteConfirm === 'hard'
              ? $t('ui.theImpactCheckConfirmsThatThisEmptyCompetitionHasNo')
              : $t('ui.afterDeletionTheCompetitionWillNotBeVisibleToThe') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deleting || hardDeleting">{{ $t('ui.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="deleting || hardDeleting"
            @click="submitDelete"
          >
            <Spinner v-if="deleting || hardDeleting" data-icon="inline-start" />
            {{ deleting || hardDeleting ? $t('ui.processing') : $t('ui.confirmDeletion') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
