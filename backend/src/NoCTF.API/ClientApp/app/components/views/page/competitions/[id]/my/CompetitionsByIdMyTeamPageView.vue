<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdMyTeamPageViewState } from '~/features/routes/competitions/[id]/my/useCompetitionsByIdMyTeamPage'

const viewProps = defineProps<{ state: CompetitionsByIdMyTeamPageViewState }>()
const { Copy, RefreshCw, maximumAppealStatementLength, minimumAppealStatementLength, competitionId, competition, tracksEnabled, canEditOrganization, selectableTracks, tracksLoading, tracksError, loadTracks, team, loading, loadError, load, isCaptain, invitationToken, invitationLoading, invitationError, loadInvitationToken, rotating, rotate, copyToken, renameOpen, renameValue, renameTrackKey, selectedRenameTrack, renameValid, renamePending, openRename, submitRename, maximumAvatarBytes, avatarInputKey, avatarPending, replaceTeamAvatar, clearTeamAvatar, transferOpen, transferTarget, transferPending, transferableMembers, submitTransfer, acting, disband, leave, banCase, appealOpen, appealStatement, appealPending, appealError, banCaseError, loadBanCase, appealStatusLabel, submitAppeal, setAppealOpen, CompetitionParticipantWorkspace, TeamMembers, TeamRuntimeManager, onInputAppealError } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId">
    <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <Skeleton v-else-if="loading" class="h-64 w-full" />

    <Empty v-else-if="!team" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('competitions.competitionsBy.description.joinedTeamCompetitionYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('competitions.competitionsBy.description.returnOverviewPageCreate') }}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button as-child>
          <NuxtLink :to="`/competitions/${competitionId}`">{{ $t('competitions.label.goOverview') }}</NuxtLink>
        </Button>
      </EmptyContent>
    </Empty>

    <template v-else>
      <Card>
        <CardHeader>
          <div class="flex flex-wrap items-center gap-3">
            <Avatar class="size-12">
              <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
              <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
            </Avatar>
            <CardTitle class="text-xl">{{ team.name }}</CardTitle>
            <Badge
              :variant="team.registrationStatus === 'Approved' ? 'default' : team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'"
            >
              {{ teamRegistrationStatusLabel(team.registrationStatus) }}
            </Badge>
            <Badge variant="outline">{{ team.trackName ?? team.trackKey }}</Badge>
            <Badge v-if="team.isLocked" variant="outline">{{ $t('competitions.label.locked') }}</Badge>
            <Badge v-if="team.isBanned" variant="destructive">{{ $t('common.label.banned') }}</Badge>
          </div>
          <CardDescription>{{ $t('competitions.label.registered', { time: formatDateTime(team.registeredAt) }) }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Alert v-if="team.registrationStatus === 'Pending'">
            <AlertDescription>{{ $t('competitions.competitionsBy.validation.pendingTeamFormat') }}</AlertDescription>
          </Alert>
        </CardContent>
      <Separator />

      <section v-if="team.isBanned" id="ban-appeal" class="scroll-mt-24">
        <CardHeader>
          <CardTitle class="text-base">{{ $t('competitions.label.banProcessing') }}</CardTitle>
          <CardDescription>{{ $t('competitions.competitionsBy.description.teamCurrentlyBannedSubmit') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Alert v-if="banCaseError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(banCaseError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadBanCase">{{ $t('common.label.reload') }}</Button>
            </AlertDescription>
          </Alert>
          <template v-if="banCase">
            <p class="text-sm">
              {{ $t('teams.ban.occurredAtPrefix') }}{{ formatDateTime(banCase.bannedAt) }} {{ $t('teams.ban.sourcePrefix') }}{{ banCase.source === 'CheatIncident' ? $t('competitions.label.cheatingDetection') : $t('competitions.label.manualProcessing') }}
            </p>
            <Alert v-if="banCase.appeal">
              <AlertDescription>
                {{ $t('competitions.label.appealStatus', { status: appealStatusLabel(banCase.appeal.status) }) }}
                <template v-if="banCase.appeal.resolutionReason">
                  · {{ $t('competitions.label.resolution', { reason: banCase.appeal.resolutionReason }) }}
                </template>
              </AlertDescription>
            </Alert>
          </template>
          <div v-if="!banCaseError && banCase?.canAppeal !== false">
            <Dialog :open="appealOpen" @update:open="setAppealOpen">
              <DialogTrigger as-child>
                <Button variant="outline">{{ $t('competitions.label.submitBanAppeal') }}</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ $t('common.label.banAppeal') }}</DialogTitle>
                  <DialogDescription>{{ $t('competitions.competitionsBy.description.stateReasonsComplaintOrganizer') }}</DialogDescription>
                </DialogHeader>
                <UiForm @submit.prevent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="appeal-statement">{{ $t('competitions.label.statementGrievance') }}</FieldLabel>
                      <Textarea
                        id="appeal-statement"
                        v-model="appealStatement"
                        rows="8"
                        :minlength="minimumAppealStatementLength"
                        :maxlength="maximumAppealStatementLength"
                        aria-describedby="appeal-requirement appeal-error"
                        required
                        @input="onInputAppealError(null)"
                      />
                      <p id="appeal-requirement" class="text-xs text-muted-foreground">
                        {{ $t('competitions.label.requiresCharacters', { minimum: minimumAppealStatementLength, maximum: maximumAppealStatementLength }) }}
                      </p>
                      <p v-if="appealError" id="appeal-error" role="alert" class="text-sm text-destructive">
                        {{ $message(appealError) }}
                      </p>
                    </Field>
                    <Field>
                      <Button type="button" class="w-full" :disabled="appealPending" @click="submitAppeal">
                        <Spinner v-if="appealPending" data-icon="inline-start" /> {{ $t('competitions.label.submitAppeal') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogContent>
            </Dialog>
          </div>
        </CardContent>
      </section>

      <Alert v-if="team.isBanned" variant="destructive">
        <AlertDescription>{{ $t('competitions.competitionsBy.validation.bannedTeamFormat') }}</AlertDescription>
      </Alert>

      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('common.label.members.teamPageView', { count: team.memberIds?.length ?? 0 }) }}</CardTitle>
          <CardDescription v-if="!isCaptain">{{ $t('competitions.competitionsBy.description.leaderRemoveMembers') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <component :is="TeamMembers"
            :competition-id="competitionId"
            :team="team"
            :can-manage="isCaptain && !team.isBanned && canEditOrganization"
            @changed="load"
          />
        </CardContent>
      </section>

      <template v-if="team.registrationStatus === 'Approved' && !team.isBanned">
        <Separator />
        <component :is="TeamRuntimeManager" :key="team.id ?? undefined" :competition-id="competitionId" />
      </template>

      <template v-if="isCaptain && !team.isBanned">
      <Separator />
      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('competitions.label.inviteMembers') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Skeleton v-if="invitationLoading" class="h-9 w-80 max-w-full" />
          <Alert v-else-if="invitationError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(invitationError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadInvitationToken">
                {{ $t('common.label.retry') }}
              </Button>
            </AlertDescription>
          </Alert>
          <div v-else-if="invitationToken" class="flex flex-wrap items-center gap-2">
            <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ invitationToken }}</code>
            <Button variant="outline" size="sm" @click="copyToken">
              <Copy data-icon="inline-start" /> {{ $t('common.action.copy') }} </Button>
          </div>
          <div>
            <Button variant="outline" :disabled="rotating" @click="rotate">
              <Spinner v-if="rotating" data-icon="inline-start" />
              <RefreshCw v-else data-icon="inline-start" /> {{ $t('competitions.label.rotateInvitationCode') }} </Button>
          </div>
        </CardContent>
      </section>
      </template>

      <template v-if="!team.isBanned">
      <Separator />
      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('common.label.teamManagement') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <template v-if="isCaptain">
            <Dialog v-model:open="renameOpen">
              <DialogTrigger as-child>
                <Button variant="outline" :disabled="!canEditOrganization" @click="openRename">{{ $t('competitions.label.editTeamInformation') }}</Button>
              </DialogTrigger>
              <DialogScrollContent class="max-h-[85vh] sm:max-w-xl">
                <DialogHeader>
                  <DialogTitle>{{ $t('competitions.label.editTeamInformation') }}</DialogTitle>
                  <DialogDescription>
                    {{ $t('competitions.competitionsBy.label.teamChangesCreateRegistration') }}
                  </DialogDescription>
                </DialogHeader>
                <UiForm validation="feature" @submit.prevent="submitRename">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="rename-input">{{ $t('competitions.label.newTeamName') }}</FieldLabel>
                      <Input id="rename-input" v-model="renameValue" required maxlength="128" />
                    </Field>
                    <Field v-if="tracksEnabled && tracksLoading">
                      <Skeleton class="h-10 w-full" />
                    </Field>
                    <Field v-else-if="tracksEnabled && tracksError">
                      <Alert variant="destructive">
                        <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
                          <span>{{ $message(tracksError) }}</span>
                          <Button type="button" size="sm" variant="outline" @click="loadTracks">{{ $t('common.label.retry') }}</Button>
                        </AlertDescription>
                      </Alert>
                    </Field>
                    <Field v-else-if="tracksEnabled">
                      <FieldLabel for="team-edit-track">{{ $t('competitions.label.competitionTrack') }}</FieldLabel>
                      <Select v-model="renameTrackKey" required>
                        <SelectTrigger id="team-edit-track"><SelectValue :placeholder="$t('common.label.selectTrack')" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem
                            v-for="track in selectableTracks"
                            :key="track.key ?? undefined"
                            :value="track.key!"
                          >
                            <span class="flex items-center gap-2">
                              <Avatar v-if="track.requiredSsoProviderIconUrl" class="size-5">
                                <AvatarImage :src="track.requiredSsoProviderIconUrl" :alt="track.requiredSsoProviderName ?? ''" />
                                <AvatarFallback>{{ track.requiredSsoProviderName?.slice(0, 1) }}</AvatarFallback>
                              </Avatar>
                              <span>{{ track.name }}</span>
                              <Badge v-if="track.requiredSsoProviderId" variant="outline">{{ track.requiredSsoProviderName }}</Badge>
                            </span>
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription v-if="selectedRenameTrack?.requiredSsoProviderId">
                        {{ $t('sso.trackGateProvider', { provider: selectedRenameTrack.requiredSsoProviderName ?? '-' }) }}
                      </FieldDescription>
                    </Field>
                    <Alert v-if="tracksEnabled && selectedRenameTrack?.meetsSsoRequirement === false">
                      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
                        <span>{{ $t('competitions.competitionOverview.label.trackRequirementsCheckedRegistration') }}</span>
                        <Button as-child type="button" size="sm" variant="outline">
                          <NuxtLink :to="{ path: '/', query: { account: 'security' } }">{{ $t('sso.openAccountSecurity') }}</NuxtLink>
                        </Button>
                      </AlertDescription>
                    </Alert>
                    <Separator />
                    <Field>
                      <FieldLabel for="team-avatar-file">{{ $t('competitions.label.teamAvatar') }}</FieldLabel>
                      <FileUpload
                        :key="avatarInputKey ?? undefined"
                        id="team-avatar-file"
                        accept="image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp"
                        :pending="avatarPending"
                        @change="replaceTeamAvatar"
                      />
                      <FieldDescription>
                        {{ maximumAvatarBytes ? $t('accountPanel.avatarRequirements', { limit: formatBytes(maximumAvatarBytes) }) : $t('common.competitionsBy.validation.avatarJpegFormat') }}
                      </FieldDescription>
                      <Button v-if="team.avatarUrl" type="button" variant="outline" :disabled="avatarPending" @click="clearTeamAvatar">
                        <Spinner v-if="avatarPending" data-icon="inline-start" />{{ $t('competitions.label.clearTeamAvatar') }}
                      </Button>
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="renamePending || !renameValid || Boolean(tracksError)">
                        <Spinner v-if="renamePending" data-icon="inline-start" /> {{ $t('common.action.save') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogScrollContent>
            </Dialog>

            <FieldDescription v-if="!canEditOrganization">{{ $t('competitions.competitionsBy.description.teamChangesClosedStage', { status: competition?.status ?? '-' }) }}</FieldDescription>

            <Dialog v-model:open="transferOpen">
              <DialogTrigger as-child>
                <Button variant="outline" :disabled="!canEditOrganization || !transferableMembers.length">{{ $t('competitions.label.transferCaptain') }}</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('competitions.label.transferCaptain') }}</DialogTitle>
                  <DialogDescription>{{ $t('competitions.competitionsBy.description.transferBecomeOrdinaryMember') }}</DialogDescription>
                </DialogHeader>
                <UiForm @submit.prevent="submitTransfer">
                  <FieldGroup>
                    <Field>
                      <FieldLabel>{{ $t('competitions.label.newCaptain') }}</FieldLabel>
                      <Select v-model="transferTarget">
                        <SelectTrigger><SelectValue :placeholder="$t('competitions.label.selectMembers')" /></SelectTrigger>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem v-for="id in transferableMembers" :key="id ?? undefined" :value="id">
                              {{ id }}
                            </SelectItem>
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="transferPending || !transferTarget">
                        <Spinner v-if="transferPending" data-icon="inline-start" /> {{ $t('common.label.confirmTransfer') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogContent>
            </Dialog>
            <p v-if="!transferableMembers.length" class="text-xs text-muted-foreground">
              {{ $t('competitions.competitionsBy.description.thereOtherTeamMembers') }}
            </p>

            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">{{ $t('competitions.label.disbandTeam') }}</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ $t('competitions.competitionsBy.label.confirmedDisbandTeam') }}</AlertDialogTitle>
                  <AlertDialogDescription> {{ $t('competitions.competitionsBy.description.disbandmentMembersRemovedTeam') }} </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
                  <AlertDialogAction :disabled="acting" @click="disband">{{ $t('competitions.label.confirmDissolution') }}</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </template>

          <AlertDialog v-else>
            <AlertDialogTrigger as-child>
              <Button variant="destructive" :disabled="!canEditOrganization">{{ $t('competitions.label.exitTeam') }}</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{{ $t('competitions.competitionsBy.label.confirmLeaveTeam') }}</AlertDialogTitle>
                <AlertDialogDescription>{{ $t('competitions.competitionsBy.description.exitingRejoinInvitationCode') }}</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
                <AlertDialogAction :disabled="acting" @click="leave">{{ $t('competitions.label.confirmExit') }}</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </CardContent>
      </section>
      </template>
      </Card>
    </template>
    </div>
  </component>
</template>
