<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdIndexPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdIndexPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdIndexPageViewState }>()
const { startGateErrorMessage, competitionId, competition, canWrite, canManagePermissions, isAdministrator, pendingAction, actionError, status, isDeleted, posterUrl, posterLoading, posterError, posterPending, posterInputKey, posterSelectionError, posterRemoveOpen, refreshPoster, selectPoster, removePoster, setPosterRemoveOpen, steps, actions, confirmTarget, execute, trigger, validating, validationErrors, validateStart, generating, generateFailures, generateMissingFlags, deleting, restoring, hardDeleting, forceDeleting, deleteConfirm, forceDeleteOpen, forceDeleteTitle, forceDeleteReason, forceDeleteError, forceDeleteConflictingIds, hardDeletePreview, hardDeletePreviewLoading, hardDeletePreviewError, hardDeleteReferenceLabel, restore, forceDeleteValid, beginForceDelete, forceDelete, submitDelete, onClickDeleteConfirm, onClickDeleteConfirm2, onUpdateOpenConfirmTarget, onClickForceDeleteOpen, onUpdateOpenDeleteConfirm } = toRefs(viewProps.state)
</script>

<template>
  <div v-if="competition" class="flex flex-col gap-6">
    <Card class="gap-0">
      <section v-if="!isDeleted" id="competition-poster-management" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('common.label.competitionPoster') }}</CardTitle>
        <CardDescription>{{ $t('administration.label.competitionPosterManagementDescription') }}</CardDescription>
      </CardHeader>
      <CardContent class="grid min-w-0 gap-5 lg:grid-cols-[minmax(16rem,24rem)_minmax(0,1fr)] lg:items-start">
        <CoverImage
          :src="posterUrl"
          :pending="posterLoading"
          :alt="$t('common.label.competitionPoster')"
          :fallback="$t('competitionBrowser.noPoster')"
          :aspect-ratio="16 / 9"
          loading="eager"
          fetchpriority="high"
          class="min-w-0 rounded-xl"
        />
        <div class="flex min-w-0 flex-col gap-4">
          <Alert v-if="posterError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(posterError) }}</span>
              <Button type="button" size="sm" variant="outline" :disabled="posterLoading" @click="refreshPoster">
                {{ $t('common.label.retry') }}
              </Button>
            </AlertDescription>
          </Alert>
          <template v-if="canWrite">
            <Field>
              <FieldLabel for="competition-poster-upload">
                {{ posterUrl ? $t('administration.label.replaceCompetitionPoster') : $t('administration.label.uploadCompetitionPoster') }}
              </FieldLabel>
              <FileUpload
                :key="posterInputKey ?? undefined"
                id="competition-poster-upload"
                accept="image/jpeg,image/png,image/webp"
                :disabled="posterPending"
                :pending="posterPending"
                :error="posterSelectionError"
                @change="selectPoster"
              />
              <FieldDescription>{{ $t('administration.label.competitionPosterUploadDescription') }}</FieldDescription>
            </Field>
            <div v-if="posterUrl">
              <Button type="button" variant="outline" size="sm" :disabled="posterPending" @click="setPosterRemoveOpen(true)">
                {{ $t('administration.label.removeCompetitionPoster') }}
              </Button>
            </div>
          </template>
          <p v-else class="text-sm text-muted-foreground">{{ $t('administration.competitionsBy.validation.roleReadFormat.indexPageView') }}</p>
        </div>
      </CardContent>
      </section>

      <Separator v-if="!isDeleted" />
      <section id="competition-lifecycle-management" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.lifeCycle') }}</CardTitle>
        <CardDescription class="font-mono tabular-nums">
          {{ adminFormatDateTime(competition.startTime) }} ~ {{ adminFormatDateTime(competition.endTime) }}
        </CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-6">
        <div class="flex flex-wrap items-center gap-2">
          <template v-for="(step, i) in steps" :key="step.value ?? undefined">
            <Badge :variant="status === step.value || (step.value === 'Running' && status === 'Paused') ? 'default' : 'outline'">
              {{ step.value === 'Running' && status === 'Paused' ? $t('common.label.suspended') : translate(step.label) }}
            </Badge>
            <span v-if="i < steps.length - 1" class="text-muted-foreground">→</span>
          </template>
        </div>

        <Alert v-if="actionError" variant="destructive">
          <AlertDescription>{{ $message(actionError) }}</AlertDescription>
        </Alert>

        <div v-if="canWrite && !isDeleted" class="flex flex-wrap items-center gap-2">
          <Button v-if="status === 'Published'" variant="outline" size="sm" :disabled="validating" @click="validateStart">
            <Spinner v-if="validating" data-icon="inline-start" /> {{ $t('administration.label.checkStart') }} </Button>
          <Button
            v-for="a in actions.filter(a => a.visible)"
            :key="a.key ?? undefined"
            size="sm"
            :variant="a.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="trigger(a)"
          >
            <Spinner v-if="pendingAction === a.key" data-icon="inline-start" />
            {{ a.label }}
          </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('administration.competitionsBy.validation.roleReadFormat') }}</p>

        <div v-if="validationErrors && validationErrors.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(ve, i) in validationErrors" :key="i ?? undefined" variant="destructive">
            <AlertDescription>
              {{ startGateErrorMessage(ve) }}
              <NuxtLink
                v-if="ve.competitionChallengeId"
                class="underline"
                :to="`/admin/competitions/${competitionId}/challenges/${ve.competitionChallengeId}`"
              > {{ $t('administration.label.viewQuestions') }} </NuxtLink>
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
      </section>

      <template v-if="canWrite && !isDeleted">
      <Separator />
      <section id="competition-flag-management" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.flagGeneration') }}</CardTitle>
        <CardDescription>{{ $t('administration.competitionsBy.description.generateMissingTeamFlags') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-3">
        <div>
          <Button variant="outline" size="sm" :disabled="generating" @click="generateMissingFlags">
            <Spinner v-if="generating" data-icon="inline-start" /> {{ $t('administration.label.generateMissingFlag') }} </Button>
        </div>
        <div v-if="generateFailures && generateFailures.length > 0" class="flex flex-col gap-2">
          <Alert v-for="(f, i) in generateFailures" :key="i ?? undefined" variant="destructive">
            <AlertDescription>
              {{ $t('administration.label.challengeTeam', { challenge: f.competitionChallengeId ?? '-', team: f.teamId ?? '-', description: f.description ?? f.code ?? '-' }) }}
            </AlertDescription>
          </Alert>
        </div>
      </CardContent>
      </section>
      </template>

      <template v-if="canWrite">
      <Separator />
      <section id="competition-danger-management" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle class="text-destructive">{{ $t('administration.label.dangerZone') }}</CardTitle>
        <CardDescription v-if="isDeleted">
          {{ $t('administration.competitionsBy.description.competitionWasSoftDeleted', { time: adminFormatDateTime(competition.deletedAt) }) }}
        </CardDescription>
        <CardDescription v-else>{{ $t('administration.competitionsBy.description.softDeletionReversiblePhysical') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col items-start gap-3">
        <div class="flex flex-wrap items-center gap-2">
          <Button v-if="!isDeleted" variant="outline" size="sm" :disabled="deleting" @click="onClickDeleteConfirm('soft')"> {{ $t('administration.label.softDeleteCompetition') }} </Button>
          <template v-if="isDeleted && canManagePermissions">
            <Button variant="outline" size="sm" :disabled="restoring" @click="restore">
              <Spinner v-if="restoring" data-icon="inline-start" /> {{ $t('administration.label.restoreDeletedContest') }} </Button>
          </template>
          <Button
            v-if="canManagePermissions && hardDeletePreview?.canHardDelete"
            variant="destructive"
            size="sm"
            :disabled="hardDeleting"
            @click="onClickDeleteConfirm2('hard')"
          > {{ $t('administration.label.deleteCompletely') }} </Button>
          <Button
            v-if="isAdministrator"
            variant="destructive"
            size="sm"
            :disabled="forceDeleting"
            @click="beginForceDelete"
          > {{ $t('administration.label.forceCascadeDelete') }} </Button>
        </div>

        <div v-if="canManagePermissions && hardDeletePreviewLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
          <Spinner data-icon="inline-start" /> {{ $t('administration.label.checkingPermanentDeletionImpact') }}
        </div>
        <div
          v-else-if="canManagePermissions && hardDeletePreviewError"
          data-slot="hard-delete-impact"
          role="status"
          class="w-full text-sm text-destructive"
        >
          {{ $message(hardDeletePreviewError) }}
        </div>
        <div
          v-else-if="canManagePermissions && hardDeletePreview && !hardDeletePreview.canHardDelete"
          data-slot="hard-delete-impact"
          role="status"
          class="flex w-full flex-col gap-2 text-sm text-muted-foreground"
        >
          <p class="font-semibold text-foreground">{{ $t('administration.label.permanentDeletionProtected') }}</p>
          <div class="flex flex-col gap-2">
            <span>{{ $t('administration.competitionsBy.description.followingPermanentHistoryBusiness') }}</span>
            <span v-if="hardDeletePreview.references?.some(reference => reference.code === 'HistoricalEvent')" class="font-medium">
              {{ $t('administration.competitionsBy.validation.competitionEventsFormat') }}
            </span>
            <span class="flex flex-wrap gap-2">
              <Badge v-for="reference in hardDeletePreview.references" :key="reference.code ?? undefined" variant="outline">
                {{ hardDeleteReferenceLabel(reference.code) }} · {{ reference.count ?? 0 }}
              </Badge>
            </span>
            <span v-if="isAdministrator" class="text-destructive">
              {{ hardDeletePreview.canForceDelete
                ? $t('administration.competitionsBy.description.platformAdministratorsForceCascade')
                : hardDeletePreview.references?.some(reference => reference.code === 'NotificationScopeConflict')
                  ? $t('administration.competitionsBy.description.forceDeletionBlockedNotification')
                  : $t('administration.competitionsBy.description.forceCascadeDeletionCurrently') }}
            </span>
          </div>
        </div>
        <p
          v-else-if="canManagePermissions && hardDeletePreview?.canHardDelete"
          data-slot="hard-delete-impact"
          role="status"
          class="w-full text-sm text-muted-foreground"
        >
          {{ $t('administration.competitionsBy.description.impactCheckPassedCompetition') }}
        </p>
      </CardContent>
      </section>
      </template>
    </Card>

    <AlertDialog :open="posterRemoveOpen" @update:open="setPosterRemoveOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.removeCompetitionPoster') }}</AlertDialogTitle>
          <AlertDialogDescription>{{ $t('administration.label.removeCompetitionPosterDescription') }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="posterPending">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="posterPending" @click="removePoster">
            <Spinner v-if="posterPending" data-icon="inline-start" />
            {{ $t('common.label.remove') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog :open="confirmTarget !== null" @update:open="onUpdateOpenConfirmTarget">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTarget?.confirm?.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmTarget?.confirm?.description }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="pendingAction !== null">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            :variant="confirmTarget?.destructive ? 'destructive' : 'default'"
            :disabled="pendingAction !== null"
            @click="confirmTarget && execute(confirmTarget)"
          >
            <Spinner v-if="pendingAction !== null" data-icon="inline-start" />
            {{ pendingAction !== null ? $t('administration.label.processing') : $t('common.label.confirm') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="forceDeleteOpen">
      <DialogContent class="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{{ $t('administration.label.forceDeleteCompetition') }}</DialogTitle>
          <DialogDescription>
            {{ $t('administration.competitionsBy.validation.undonePermanentlyFormat') }}
          </DialogDescription>
        </DialogHeader>
        <Alert v-if="forceDeleteError" variant="destructive">
          <AlertDescription>
            {{ $message(forceDeleteError) }}
            <span v-if="forceDeleteConflictingIds.length" class="mt-2 block break-all font-mono text-xs">
              {{ $t('administration.label.conflictingNotificationIds') }}：{{ forceDeleteConflictingIds.join(', ') }}
            </span>
          </AlertDescription>
        </Alert>
        <FieldGroup>
          <Field>
            <FieldLabel for="force-delete-title">{{ $t('administration.competitionsBy.description.enterFullCompetitionTitle') }}</FieldLabel>
            <Input
              id="force-delete-title"
              v-model="forceDeleteTitle"
              autocomplete="off"
              :placeholder="competition.title"
              :disabled="forceDeleting"
            />
            <FieldError v-if="forceDeleteTitle && forceDeleteTitle !== competition.title">
              {{ $t('administration.competitionsBy.validation.competitionTitleFormat') }}
            </FieldError>
          </Field>
          <Field>
            <FieldLabel for="force-delete-reason">{{ $t('administration.label.reasonDeletion') }}</FieldLabel>
            <Textarea
              id="force-delete-reason"
              v-model="forceDeleteReason"
              rows="4"
              maxlength="500"
              :disabled="forceDeleting"
            />
            <FieldDescription>{{ $t('administration.competitionsBy.description.leastCharactersReasonRecorded') }}</FieldDescription>
            <FieldError v-if="forceDeleteReason && forceDeleteReason.trim().length < 8">
              {{ $t('administration.competitionsBy.validation.deletionReasonLength') }}
            </FieldError>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="forceDeleting" @click="onClickForceDeleteOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button variant="destructive" :disabled="forceDeleting || !forceDeleteValid" @click="forceDelete">
            <Spinner v-if="forceDeleting" data-icon="inline-start" />
            {{ forceDeleting ? $t('administration.label.permanentlyDeleting') : $t('administration.label.confirmForceDeletion') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="deleteConfirm !== null" @update:open="onUpdateOpenDeleteConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ deleteConfirm === 'hard' ? $t('administration.label.completelyDeleteContest') : $t('administration.label.deleteContest') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ deleteConfirm === 'hard'
              ? $t('administration.competitionsBy.description.impactCheckConfirmsEmpty')
              : $t('administration.competitionsBy.description.deletionCompetitionVisible') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="deleting || hardDeleting">{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="deleting || hardDeleting"
            @click="submitDelete"
          >
            <Spinner v-if="deleting || hardDeleting" data-icon="inline-start" />
            {{ deleting || hardDeleting ? $t('administration.label.processing') : $t('administration.label.confirmDeletion') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
