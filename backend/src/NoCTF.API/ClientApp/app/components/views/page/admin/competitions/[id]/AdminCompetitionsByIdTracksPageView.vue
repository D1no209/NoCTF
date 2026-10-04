<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdTracksPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTracksPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdTracksPageViewState }>()
const { Plus, Save, Trash2, canWrite, mode, enabled, canUpdate, tracks, ssoProviders, pendingRemoval, removalTargets, disableConfirmationOpen, disableAffectedTeamCount, loading, saving, error, load, addTrack, requestRemoveTrack, closeRemoval, confirmRemoveTrack, requestEnabled, confirmDisable, setDisableConfirmationOpen, updateDefault, updatePublicSelectable, updateInternal, updateInvitationRequired, updateRequiredSsoProvider, configureSsoProviders, save } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-5">
    <header class="flex flex-wrap items-end justify-between gap-3 border-b pb-4">
      <h2 class="text-xl font-semibold">{{ $t('administration.label.trackConfiguration') }}</h2>
      <div v-if="canWrite && canUpdate" class="flex gap-2">
        <Button variant="outline" @click="addTrack">
          <Plus data-icon="inline-start" /> {{ $t('administration.label.addTrack') }}
        </Button>
        <Button :disabled="saving || !tracks.length" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" />
          <Save v-else data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }}
        </Button>
      </div>
    </header>

    <Field orientation="horizontal" class="rounded-md border p-4">
      <Switch
        id="tracks-enabled"
        :model-value="enabled"
        :disabled="!canWrite || !canUpdate || saving"
        @update:model-value="requestEnabled($event === true)"
      />
      <FieldContent>
        <FieldLabel for="tracks-enabled">{{ $t('administration.label.enableCompetitionTracks') }}</FieldLabel>
        <FieldDescription>
          {{ enabled ? $t('administration.label.competitionTracksEnabledDescription') : $t('administration.label.competitionTracksDisabledDescription') }}
        </FieldDescription>
      </FieldContent>
    </Field>
    <Alert v-if="!canUpdate">
      <AlertDescription>{{ $t('administration.competitionTrack.label.finishedCompetitionTracksRead') }}</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3">
        <span>{{ $message(error) }}</span>
        <Button size="sm" variant="outline" @click="load">{{ $t('common.label.retry') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-if="!loading && ssoProviders.length === 0">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('sso.trackGateNoProviders') }}</span>
        <Button size="sm" variant="outline" @click="configureSsoProviders">
          {{ $t('sso.manageIdentityProviders') }}
        </Button>
      </AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <ScrollSurface as="div" axis="x" v-else class="overflow-x-auto border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="min-w-40">{{ $t('administration.label.trackKey') }}</TableHead>
            <TableHead class="min-w-40">{{ $t('administration.label.displayName') }}</TableHead>
            <TableHead>{{ $t('administration.label.default.tracksPageView') }}</TableHead>
            <TableHead>{{ $t('administration.label.publicSelection') }}</TableHead>
            <TableHead>{{ $t('administration.label.internal') }}</TableHead>
            <TableHead>{{ $t('administration.label.scoring') }}</TableHead>
            <TableHead>{{ $t('administration.label.bloodAwards') }}</TableHead>
            <TableHead>{{ $t('administration.label.dynamicScoring') }}</TableHead>
            <TableHead>{{ $t('administration.label.leaderboardVisibility') }}</TableHead>
            <TableHead>{{ $t('administration.label.competitiveResults') }}</TableHead>
            <TableHead class="min-w-64">{{ $t('common.label.trackInvitationCode') }}</TableHead>
            <TableHead class="min-w-56">{{ $t('sso.trackGate') }}</TableHead>
            <TableHead v-if="canWrite && canUpdate" class="w-14"><span class="sr-only">{{ $t('common.label.actions') }}</span></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="(track, index) in tracks" :key="track.clientId ?? undefined">
            <TableCell>
              <Input v-model="track.key" :disabled="!canWrite || !canUpdate || track.existingKey !== null" maxlength="64" class="font-mono" />
            </TableCell>
            <TableCell>
              <Input v-model="track.name" :disabled="!canWrite || !canUpdate" maxlength="80" />
            </TableCell>
            <TableCell><Checkbox :model-value="track.isDefault" :disabled="!canWrite || !canUpdate" @update:model-value="updateDefault(index, $event)" /></TableCell>
            <TableCell><Checkbox :model-value="track.isPublicSelectable" :disabled="(!canWrite || !canUpdate || track.isInternal) ?? undefined" @update:model-value="updatePublicSelectable(track, $event)" /></TableCell>
            <TableCell><Checkbox :model-value="track.isInternal" :disabled="!canWrite || !canUpdate" @update:model-value="updateInternal(track, $event)" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsScore" :disabled="(!canWrite || !canUpdate || track.isInternal) ?? undefined" /></TableCell>
            <TableCell><Checkbox v-model="track.earnsBlood" :disabled="!canWrite || !canUpdate || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsDynamicChallengeScore" :disabled="!canWrite || !canUpdate || track.isInternal || mode !== 'Ctf'" /></TableCell>
            <TableCell><Checkbox v-model="track.visibleOnLeaderboard" :disabled="(!canWrite || !canUpdate || track.isInternal) ?? undefined" /></TableCell>
            <TableCell><Checkbox v-model="track.affectsCompetitiveResults" :disabled="(!canWrite || !canUpdate || track.isInternal) ?? undefined" /></TableCell>
            <TableCell>
              <div class="flex min-w-64 flex-col gap-2">
                <div class="flex items-center gap-2">
                  <Switch
                    :id="`track-invitation-${track.clientId}`"
                    :model-value="track.requiresInvitationCode"
                    :disabled="!canWrite || !canUpdate || track.isInternal || !track.isPublicSelectable"
                    :aria-label="$t('administration.competitionsBy.label.requireTrackInvitationCode')"
                    @update:model-value="updateInvitationRequired(track, $event)"
                  />
                  <FieldLabel :for="`track-invitation-${track.clientId}`" class="whitespace-nowrap text-xs">
                    {{ track.requiresInvitationCode ? $t('administration.competitionsBy.label.requireTrackInvitationCode') : $t('administration.label.restriction') }}
                  </FieldLabel>
                </div>
                <Input
                  v-if="track.requiresInvitationCode"
                  v-model="track.invitationCode"
                  type="password"
                  minlength="8"
                  maxlength="128"
                  :disabled="!canWrite || !canUpdate"
                  :placeholder="track.invitationCodeConfigured ? $t('administration.competitionsBy.description.leaveBlankKeepInvitation') : $t('administration.competitionsBy.label.enterCharacterInvitationCode')"
                />
              </div>
            </TableCell>
            <TableCell>
              <Select
                :model-value="track.requiredSsoProviderId ?? 'none'"
                :disabled="!canWrite || !canUpdate"
                @update:model-value="updateRequiredSsoProvider(track, String($event))"
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">{{ $t('administration.label.restriction') }}</SelectItem>
                  <SelectItem v-for="provider in ssoProviders" :key="provider.id ?? undefined" :value="provider.id!">
                    <span class="flex items-center gap-2">
                      <img v-if="provider.iconUrl" :src="provider.iconUrl" class="size-4 object-contain" alt="" aria-hidden="true" referrerpolicy="no-referrer">
                      <span>{{ provider.name }}</span>
                      <Badge v-if="!provider.enabled || !provider.allowBinding" variant="outline">{{ $t('administration.label.disabled') }}</Badge>
                    </span>
                  </SelectItem>
                </SelectContent>
              </Select>
            </TableCell>
            <TableCell v-if="canWrite && canUpdate">
              <Button variant="ghost" size="icon" :disabled="track.isDefault" :aria-label="$t('administration.label.deleteTrack')" @click="requestRemoveTrack(index)">
                <Trash2 class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </ScrollSurface>
    <p v-if="!loading" class="text-xs text-muted-foreground">
      {{ $t('administration.competitionsBy.description.bloodAwardsRequireScoring') }}
    </p>
    <p v-if="!loading" class="text-xs text-muted-foreground">{{ $t('sso.trackGateDescription') }}</p>
    <p v-if="!loading" class="text-xs text-muted-foreground">{{ $t('sso.trackGateConfigurationHint') }}</p>

    <Dialog :open="pendingRemoval !== null" @update:open="closeRemoval">
      <DialogContent v-if="pendingRemoval">
        <DialogHeader>
          <DialogTitle>{{ $t('administration.competitionsBy.label.reassignTeamsDeletingTrack') }}</DialogTitle>
          <DialogDescription>
            {{ $t('administration.label.trackDeletionAffectsTeams', { track: pendingRemoval.name, count: pendingRemoval.affectedTeamCount }) }}
          </DialogDescription>
        </DialogHeader>
        <Field>
          <FieldLabel for="track-removal-target">{{ $t('administration.label.targetTrack') }}</FieldLabel>
          <Select v-model="pendingRemoval.toTrackKey">
            <SelectTrigger id="track-removal-target"><SelectValue :placeholder="$t('common.label.selectTrack')" /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="track in removalTargets" :key="track.clientId ?? undefined" :value="track.key">
                {{ track.name }}
              </SelectItem>
            </SelectContent>
          </Select>
          <FieldDescription>{{ $t('administration.label.trackDeletionReprojectsHistory') }}</FieldDescription>
        </Field>
        <DialogFooter>
          <Button variant="outline" @click="closeRemoval(false)">{{ $t('common.action.cancel') }}</Button>
          <Button variant="destructive" :disabled="!pendingRemoval.toTrackKey" @click="confirmRemoveTrack">
            {{ $t('administration.label.confirmReassignmentDelete') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <AlertDialog :open="disableConfirmationOpen" @update:open="setDisableConfirmationOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.disableCompetitionTracks') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.label.disableTracksMergeWarning', { count: disableAffectedTeamCount }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <AlertDialogAction @click="confirmDisable">{{ $t('administration.label.continueSave') }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
