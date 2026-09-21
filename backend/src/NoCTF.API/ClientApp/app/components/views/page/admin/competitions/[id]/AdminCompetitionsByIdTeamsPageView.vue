<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdTeamsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTeamsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdTeamsPageViewState }>()
const { competitionId, canJudge, canWrite, teams, search, loading, pageLoading, error, page, pageCount, total, pageLimit, loadPage, setPageSize, pendingId, tracks, tracksEnabled, selectedTeam, teamMembers, teamDetailLoading, teamDisplayNames, displayTeamName, scoreAdjustmentTeam, scoreAdjustmentChallenges, scoreAdjustmentChallengeId, scoreAdjustmentDelta, scoreAdjustmentLoading, scoreAdjustmentPending, scoreAdjustmentError, scoreAdjustmentValid, openScoreAdjustment, closeScoreAdjustment, submitScoreAdjustment, openTeamDetail, assignTrackValue, simpleAction, banDialog, banReason, banAnnouncePublicly, banPending, banReasonValid, openBan, submitBan, appeals, appealsLoading, appealsError, appealDialog, appealReason, appealPending, openAppeal, submitAppeal, PrivateAccountPanel, onUpdateOpenOpen, onClickScoreAdjustmentTeam, onUpdateOpenBanDialog, onClickBanDialog, onUpdateOpenAppealDialog, onClickAppealDialog } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-8">
    <div id="ban-appeals" class="flex scroll-mt-24 flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('ui.teamManagement2') }}</h2>
      <Input v-model="search" :placeholder="$t('ui.searchTeam')" :aria-label="$t('ui.searchTeam')" />
      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ $message(error) }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading" class="h-48 w-full" />
      <Empty v-else-if="teams.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.thereIsNoRegisteredTeamYet') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('ui.teamName') }}</TableHead>
            <TableHead v-if="tracksEnabled" class="min-w-36">{{ $t('ui.tracks') }}</TableHead>
            <TableHead class="w-24">{{ $t('ui.numberOfPeople') }}</TableHead>
            <TableHead class="w-28">{{ $t('ui.registrationStatus') }}</TableHead>
            <TableHead class="w-28">{{ $t('ui.banStatus') }}</TableHead>
            <TableHead class="w-44">{{ $t('ui.registrationTime') }}</TableHead>
            <TableHead v-if="canWrite || canJudge" class="w-64 text-right">{{ $t('ui.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in teams" :key="t.id">
            <TableCell v-if="tracksEnabled">
              <ActionButton
                type="button"
                class="rounded-sm font-medium underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                @click="openTeamDetail(t)"
              >
                {{ displayTeamName(t) }}
              </ActionButton>
            </TableCell>
            <TableCell>
              <Select
                v-if="canWrite"
                :model-value="t.trackKey"
                :disabled="pendingId === t.id"
                @update:model-value="assignTrackValue(t, $event)"
              >
                <SelectTrigger class="min-w-32"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="track in tracks" :key="track.key" :value="track.key!">
                    {{ track.name }}<template v-if="track.isInternal"> · {{ $t('ui.internal') }}</template>
                  </SelectItem>
                </SelectContent>
              </Select>
              <Badge v-else variant="outline">{{ t.trackName ?? t.trackKey }}</Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ t.memberIds?.length ?? 0 }}</TableCell>
            <TableCell>
              <Badge :variant="t.registrationStatus === 'Approved' ? 'default' : t.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                {{ enumLabel(TeamRegistrationStatusLabel, t.registrationStatus) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="t.isBanned" variant="destructive">{{ $t('ui.banned2') }}</Badge>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(t.registeredAt) }}</TableCell>
            <TableCell v-if="canWrite || canJudge" class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <template v-if="canWrite && t.registrationStatus === 'Pending'">
                  <Button size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'approve')">{{ $t('ui.pass') }}</Button>
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'reject')">{{ $t('ui.reject') }}</Button>
                </template>
                <Button v-else-if="canWrite" variant="outline" size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'pending')">
                  {{ $t('ui.returnToPendingReview') }}
                </Button>
                <Button v-if="canJudge" variant="outline" size="sm" :disabled="pendingId === t.id" @click="openScoreAdjustment(t)">
                  {{ $t('ui.adjustScore') }}
                </Button>
                <template v-if="canJudge && !t.isBanned">
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="openBan(t, 'ban')">{{ $t('ui.ban') }}</Button>
                </template>
                <template v-else-if="canWrite">
                  <Button type="button" variant="ghost" size="sm" :disabled="pendingId === t.id" @click.stop="openBan(t, 'correct')">{{ $t('ui.correctBan') }}</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <OffsetPagination
        v-if="teams.length > 0 || total > 0"
        :page="page"
        :page-count="pageCount"
        :total="total"
        :limit="pageLimit"
        :loading="pageLoading"
        @update:page="loadPage"
        @update:limit="setPageSize"
      />
    </div>

    <Sheet :open="selectedTeam !== null" @update:open="onUpdateOpenOpen">
      <SheetContent data-scroll-surface class="overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{{ $t('ui.teamDetails') }}</SheetTitle>
          <SheetDescription>{{ selectedTeam ? displayTeamName(selectedTeam) : '' }}</SheetDescription>
        </SheetHeader>
        <div v-if="selectedTeam" class="mt-6 flex flex-col gap-6">
          <dl class="grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
            <dt v-if="tracksEnabled" class="text-muted-foreground">{{ $t('ui.tracks') }}</dt>
            <dd v-if="tracksEnabled">{{ selectedTeam.trackName ?? selectedTeam.trackKey }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.registrationStatus') }}</dt>
            <dd>{{ enumLabel(TeamRegistrationStatusLabel, selectedTeam.registrationStatus) }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.banStatus') }}</dt>
            <dd>{{ selectedTeam.isBanned ? $t('ui.banned2') : $t('ui.normal') }}</dd>
            <dt class="text-muted-foreground">{{ $t('ui.registrationTime') }}</dt>
            <dd class="font-mono tabular-nums">{{ adminFormatDateTime(selectedTeam.registeredAt) }}</dd>
          </dl>
          <Separator />
          <section class="flex flex-col gap-3">
            <h3 class="font-semibold">{{ $t('ui.members3', { count: selectedTeam.memberIds?.length ?? 0 }) }}</h3>
            <div v-if="teamDetailLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
              <Spinner class="size-4" />{{ $t('ui.loading2') }}
            </div>
            <div v-else class="flex flex-col divide-y rounded-md border">
              <div v-for="member in teamMembers" :key="member.userId" class="flex flex-wrap items-center gap-3 p-3">
                <Avatar class="size-9">
                  <AvatarImage v-if="member.avatarUrl" :src="member.avatarUrl" :alt="member.userName ?? ''" />
                  <AvatarFallback>{{ member.userName?.slice(0, 2) }}</AvatarFallback>
                </Avatar>
                <div class="min-w-0 flex-1">
                  <NuxtLink :to="`/users/${member.userId}`" class="font-medium hover:underline">
                    {{ member.userName }}
                  </NuxtLink>
                </div>
                <Badge v-if="member.userId === selectedTeam.captainId" variant="secondary">{{ $t('ui.captain') }}</Badge>
                <component
                  :is="PrivateAccountPanel"
                  v-if="canJudge && member.userId && selectedTeam.id"
                  class="w-full pt-3"
                  :user-id="member.userId"
                  :competition-id="competitionId"
                  :team-id="selectedTeam.id"
                  :show-activities="false"
                />
              </div>
              <p v-if="!teamMembers.length" class="p-3 text-sm text-muted-foreground">{{ $t('ui.noMembers') }}</p>
            </div>
          </section>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="scoreAdjustmentTeam !== null" @update:open="closeScoreAdjustment">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.scoreAdjustment') }}</DialogTitle>
          <DialogDescription>
            {{ $t('ui.recordAChallengeScoreAdjustmentForTeamPositiveValuesAdd', { team: scoreAdjustmentTeam ? displayTeamName(scoreAdjustmentTeam) : '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="scoreAdjustmentError" variant="destructive">
            <AlertDescription>{{ $message(scoreAdjustmentError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="score-adjustment-challenge">{{ $t('ui.challenge') }}</FieldLabel>
            <Skeleton v-if="scoreAdjustmentLoading" class="h-10 w-full" />
            <Select v-else v-model="scoreAdjustmentChallengeId">
              <SelectTrigger id="score-adjustment-challenge" class="w-full">
                <SelectValue :placeholder="$t('ui.selectAChallenge')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="challenge in scoreAdjustmentChallenges" :key="challenge.id" :value="challenge.id!">
                  {{ challenge.title }} · {{ directionLabel(challenge.direction) }}
                </SelectItem>
              </SelectContent>
            </Select>
            <FieldDescription v-if="!scoreAdjustmentLoading && scoreAdjustmentChallenges.length === 0">
              {{ $t('ui.thisCompetitionHasNoChallengesAvailableForAdjustment') }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="score-adjustment-delta">{{ $t('ui.scoreDelta') }}</FieldLabel>
            <NumberInput id="score-adjustment-delta" v-model.number="scoreAdjustmentDelta"  step="1" />
            <FieldDescription>{{ $t('ui.enterANonZeroIntegerSuchAs25Or10') }}</FieldDescription>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="scoreAdjustmentPending" @click="onClickScoreAdjustmentTeam(null)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="scoreAdjustmentLoading || scoreAdjustmentPending || !scoreAdjustmentValid" @click="submitScoreAdjustment">
            <Spinner v-if="scoreAdjustmentPending" data-icon="inline-start" />
            {{ $t('ui.confirmAdjustment') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Separator />

    <div class="flex flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('ui.banAppeal') }}</h2>
      <Alert v-if="appealsError" variant="destructive">
        <AlertDescription>{{ $message(appealsError) }}</AlertDescription>
      </Alert>
      <Skeleton v-if="appealsLoading" class="h-32 w-full" />
      <Empty v-else-if="!appealsError && appeals.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.noAppealYet') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <div v-else-if="appeals.length > 0" class="flex flex-col gap-3">
        <Card v-for="a in appeals" :id="`appeal-${a.appeal?.id ?? a.banEventId}`" :key="a.banEventId" class="scroll-mt-24">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <CardTitle class="text-base">{{ teamDisplayName(a, teamDisplayNames) }}</CardTitle>
              <div class="flex items-center gap-1">
                <Badge variant="outline">{{ enumLabel(TeamBanSourceLabel, a.source) }}</Badge>
                <Badge v-if="a.appeal" :variant="a.appeal.status === 'Submitted' ? 'secondary' : a.appeal.status === 'Accepted' ? 'default' : 'destructive'">
                  {{ $t('ui.appeal', { status: enumLabel(TeamBanAppealStatusLabel, a.appeal.status) }) }}
                </Badge>
                <Badge v-if="a.isCurrentlyBanned" variant="destructive">{{ $t('ui.banned') }}</Badge>
              </div>
            </div>
            <CardDescription>{{ $t('ui.bannedAt', { time: adminFormatDateTime(a.bannedAt) }) }}</CardDescription>
          </CardHeader>
          <CardContent v-if="a.appeal" class="flex flex-col gap-2 text-sm">
            <p><span class="text-muted-foreground">{{ $t('ui.complainant') }}</span>{{ a.appeal.submittedByUserName }} · {{ adminFormatDateTime(a.appeal.submittedAt) }}</p>
            <p class="whitespace-pre-wrap">{{ a.appeal.statement }}</p>
            <p v-if="a.appeal.resolutionReason" class="text-muted-foreground">
              {{ $t('ui.resolvedBy2', { user: a.appeal.resolvedByUserName ?? '-', time: adminFormatDateTime(a.appeal.resolvedAt), reason: a.appeal.resolutionReason }) }}
            </p>
          </CardContent>
          <CardFooter v-if="canJudge && a.appeal?.status === 'Submitted' && a.canResolve" class="gap-2">
            <Button size="sm" :disabled="appealPending" @click="openAppeal(a, 'accept')">{{ $t('ui.acceptAppealUnblock') }}</Button>
            <Button variant="outline" size="sm" :disabled="appealPending" @click="openAppeal(a, 'uphold')">{{ $t('ui.maintainBan') }}</Button>
          </CardFooter>
        </Card>
      </div>
    </div>

    <Dialog :open="banDialog !== null" @update:open="onUpdateOpenBanDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ banDialog?.mode === 'ban' ? $t('ui.banTeam') : $t('ui.correctBan') }}</DialogTitle>
          <DialogDescription>
            {{ banDialog?.mode === 'ban'
              ? $t('ui.banTheTeamWillNoLongerBeAbleToCompete', { team: banDialog?.team.name ?? '-' })
              : $t('ui.markTheBanOfAsIncorrectAndPublishACorrection', { team: banDialog?.team.name ?? '-' }) }}
            {{ $t('ui.aReasonIsRequiredAndWillBeAudited') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="ban-reason">{{ $t('ui.reason') }}</FieldLabel>
            <Textarea id="ban-reason" v-model="banReason" maxlength="512" required />
            <FieldDescription v-if="banDialog?.mode === 'correct'">
              {{ $t('ui.atLeast8CharactersCurrently512', { length: banReason.trim().length }) }}
            </FieldDescription>
            <FieldDescription v-else>
              {{ $t('ui.currently512', { length: banReason.trim().length }) }}
            </FieldDescription>
          </Field>
          <Field v-if="banDialog?.mode === 'ban'" orientation="horizontal">
            <Checkbox id="ban-announce-publicly" v-model="banAnnouncePublicly" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="ban-announce-publicly">{{ $t('ui.releaseOfEventDisciplineAnnouncementAfterBan') }}</FieldLabel>
              <FieldDescription> {{ $t('ui.offByDefaultAfterOpeningParticipantsWillBeNotifiedThat') }} </FieldDescription>
            </div>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickBanDialog(null)">{{ $t('ui.cancel') }}</Button>
          <Button type="button" :disabled="banPending || !banReasonValid" @click="submitBan">
            <Spinner v-if="banPending" data-icon="inline-start" /> {{ $t('ui.confirm') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="appealDialog !== null" @update:open="onUpdateOpenAppealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ appealDialog?.mode === 'accept' ? $t('ui.acceptAppeal') : $t('ui.maintainBan') }}</DialogTitle>
          <DialogDescription>
            {{ appealDialog?.mode === 'accept'
              ? $t('ui.acceptSAppealAndLiftTheBan', { team: appealDialog?.banCase.teamName ?? '-' })
              : $t('ui.rejectSAppealAndUpholdTheBan', { team: appealDialog?.banCase.teamName ?? '-' }) }}
            {{ $t('ui.aResolutionReasonIsRequired') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="appeal-reason">{{ $t('ui.reasonsForRuling') }}</FieldLabel>
            <Textarea id="appeal-reason" v-model="appealReason" required />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickAppealDialog(null)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="appealPending || !appealReason.trim()" @click="submitAppeal">
            <Spinner v-if="appealPending" data-icon="inline-start" /> {{ $t('ui.confirm') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
