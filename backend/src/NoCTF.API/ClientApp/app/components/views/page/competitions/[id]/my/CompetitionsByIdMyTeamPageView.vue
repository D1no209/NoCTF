<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdMyTeamPageViewState } from '~/features/routes/competitions/[id]/my/useCompetitionsByIdMyTeamPage'

const viewProps = defineProps<{ state: CompetitionsByIdMyTeamPageViewState }>()
const { Copy, RefreshCw, maximumAppealStatementLength, minimumAppealStatementLength, competitionId, team, loading, loadError, load, isCaptain, invitationToken, invitationLoading, invitationError, loadInvitationToken, rotating, rotate, copyToken, renameOpen, renameValue, renamePending, openRename, submitRename, transferOpen, transferTarget, transferPending, transferableMembers, submitTransfer, acting, disband, leave, resubmit, banCase, appealOpen, appealStatement, appealPending, appealError, banCaseError, loadBanCase, appealStatusLabel, submitAppeal, setAppealOpen, CompetitionParticipantWorkspace, TeamMembers, onInputAppealError } = toRefs(viewProps.state)
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
        <EmptyTitle>{{ $t('ui.youHaveNotJoinedTheTeamForThisCompetitionYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.returnToTheOverviewPageToCreateATeamOr') }}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button as-child>
          <NuxtLink :to="`/competitions/${competitionId}`">{{ $t('ui.goToOverview') }}</NuxtLink>
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
            <Badge v-if="team.isLocked" variant="outline">{{ $t('ui.locked') }}</Badge>
            <Badge v-if="team.isBanned" variant="destructive">{{ $t('ui.banned2') }}</Badge>
          </div>
          <CardDescription>{{ $t('ui.registeredAt', { time: formatDateTime(team.registeredAt) }) }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Alert v-if="team.registrationStatus === 'Rejected'">
            <AlertDescription class="flex flex-wrap items-center gap-2">
              {{ $t('ui.message13') }}{{ isCaptain ? $t('ui.youCanResubmitAfterModifyingTheInformation') : $t('ui.pleaseContactTheTeamLeaderToResubmitYourRegistration') }}
              <Button v-if="isCaptain" size="sm" :disabled="acting" @click="resubmit">
                <Spinner v-if="acting" data-icon="inline-start" /> {{ $t('ui.resubmitRegistration') }} </Button>
            </AlertDescription>
          </Alert>
        </CardContent>
      <Separator />

      <section v-if="team.isBanned" id="ban-appeal" class="scroll-mt-24">
        <CardHeader>
          <CardTitle class="text-base">{{ $t('ui.banProcessing') }}</CardTitle>
          <CardDescription>{{ $t('ui.yourTeamIsCurrentlyBannedYouCanSubmitAnAppeal') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Alert v-if="banCaseError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(banCaseError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadBanCase">{{ $t('ui.reload') }}</Button>
            </AlertDescription>
          </Alert>
          <template v-if="banCase">
            <p class="text-sm">
              {{ $t('ui.message14') }}{{ formatDateTime(banCase.bannedAt) }} {{ $t('ui.message15') }}{{ banCase.source === 'CheatIncident' ? $t('ui.cheatingDetection') : $t('ui.manualProcessing') }}
            </p>
            <Alert v-if="banCase.appeal">
              <AlertDescription>
                {{ $t('ui.appealStatus', { status: appealStatusLabel(banCase.appeal.status) }) }}
                <template v-if="banCase.appeal.resolutionReason">
                  · {{ $t('ui.resolution', { reason: banCase.appeal.resolutionReason }) }}
                </template>
              </AlertDescription>
            </Alert>
          </template>
          <div v-if="!banCaseError && banCase?.canAppeal !== false">
            <Dialog :open="appealOpen" @update:open="setAppealOpen">
              <DialogTrigger as-child>
                <Button variant="outline">{{ $t('ui.submitABanAppeal') }}</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ $t('ui.banAppeal') }}</DialogTitle>
                  <DialogDescription>{{ $t('ui.stateTheReasonsForYourComplaintToTheOrganizerAnd') }}</DialogDescription>
                </DialogHeader>
                <UiForm @submit.prevent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="appeal-statement">{{ $t('ui.statementOfGrievance') }}</FieldLabel>
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
                        {{ $t('ui.requiresToCharacters', { minimum: minimumAppealStatementLength, maximum: maximumAppealStatementLength }) }}
                      </p>
                      <p v-if="appealError" id="appeal-error" role="alert" class="text-sm text-destructive">
                        {{ $message(appealError) }}
                      </p>
                    </Field>
                    <Field>
                      <Button type="button" class="w-full" :disabled="appealPending" @click="submitAppeal">
                        <Spinner v-if="appealPending" data-icon="inline-start" /> {{ $t('ui.submitAppeal') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogContent>
            </Dialog>
          </div>
        </CardContent>
      </section>

      <Alert v-if="team.isBanned" variant="destructive">
        <AlertDescription>{{ $t('ui.aBannedTeamCannotChangeItsNameMembershipCaptainInvitation') }}</AlertDescription>
      </Alert>

      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('ui.members3', { count: team.memberIds?.length ?? 0 }) }}</CardTitle>
          <CardDescription v-if="!isCaptain">{{ $t('ui.onlyTheLeaderCanRemoveMembers') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <component :is="TeamMembers"
            :competition-id="competitionId"
            :team="team"
            :can-manage="isCaptain && !team.isBanned"
            @changed="load"
          />
        </CardContent>
      </section>

      <template v-if="isCaptain && !team.isBanned">
      <Separator />
      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('ui.inviteMembers') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Skeleton v-if="invitationLoading" class="h-9 w-80 max-w-full" />
          <Alert v-else-if="invitationError" variant="destructive">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(invitationError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadInvitationToken">
                {{ $t('ui.retry') }}
              </Button>
            </AlertDescription>
          </Alert>
          <div v-else-if="invitationToken" class="flex flex-wrap items-center gap-2">
            <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ invitationToken }}</code>
            <Button variant="outline" size="sm" @click="copyToken">
              <Copy data-icon="inline-start" /> {{ $t('ui.copy') }} </Button>
          </div>
          <div>
            <Button variant="outline" :disabled="rotating" @click="rotate">
              <Spinner v-if="rotating" data-icon="inline-start" />
              <RefreshCw v-else data-icon="inline-start" /> {{ $t('ui.rotateInvitationCode') }} </Button>
          </div>
        </CardContent>
      </section>
      </template>

      <template v-if="!team.isBanned">
      <Separator />
      <section>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('ui.teamManagement') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <template v-if="isCaptain">
            <Dialog v-model:open="renameOpen">
              <DialogTrigger as-child>
                <Button variant="outline" @click="openRename">{{ $t('ui.modifyTeamName') }}</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('ui.modifyTeamName') }}</DialogTitle>
                </DialogHeader>
                <UiForm @submit.prevent="submitRename">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="rename-input">{{ $t('ui.newTeamName') }}</FieldLabel>
                      <Input id="rename-input" v-model="renameValue" required maxlength="64" />
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="renamePending">
                        <Spinner v-if="renamePending" data-icon="inline-start" /> {{ $t('ui.save') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogContent>
            </Dialog>

            <Dialog v-model:open="transferOpen">
              <DialogTrigger as-child>
                <Button variant="outline" :disabled="!transferableMembers.length">{{ $t('ui.transferCaptain') }}</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('ui.transferCaptain') }}</DialogTitle>
                  <DialogDescription>{{ $t('ui.afterTheTransferYouWillBecomeAnOrdinaryMember') }}</DialogDescription>
                </DialogHeader>
                <UiForm @submit.prevent="submitTransfer">
                  <FieldGroup>
                    <Field>
                      <FieldLabel>{{ $t('ui.newCaptain') }}</FieldLabel>
                      <Select v-model="transferTarget">
                        <SelectTrigger><SelectValue :placeholder="$t('ui.selectMembers')" /></SelectTrigger>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem v-for="id in transferableMembers" :key="id" :value="id">
                              {{ id }}
                            </SelectItem>
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="transferPending || !transferTarget">
                        <Spinner v-if="transferPending" data-icon="inline-start" /> {{ $t('ui.confirmTransfer') }} </Button>
                    </Field>
                  </FieldGroup>
                </UiForm>
              </DialogContent>
            </Dialog>
            <p v-if="!transferableMembers.length" class="text-xs text-muted-foreground">
              {{ $t('ui.thereAreNoOtherTeamMembersToTransferTheCaptain') }}
            </p>

            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">{{ $t('ui.disbandTheTeam') }}</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ $t('ui.confirmedToDisbandTheTeam') }}</AlertDialogTitle>
                  <AlertDialogDescription> {{ $t('ui.afterDisbandmentAllMembersWillBeRemovedTheTeamS') }} </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
                  <AlertDialogAction :disabled="acting" @click="disband">{{ $t('ui.confirmDissolution') }}</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </template>

          <AlertDialog v-else>
            <AlertDialogTrigger as-child>
              <Button variant="destructive">{{ $t('ui.exitTheTeam') }}</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{{ $t('ui.confirmToLeaveTheTeam') }}</AlertDialogTitle>
                <AlertDialogDescription>{{ $t('ui.afterExitingYouCanRejoinWithTheInvitationCodeOr') }}</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
                <AlertDialogAction :disabled="acting" @click="leave">{{ $t('ui.confirmToExit') }}</AlertDialogAction>
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
