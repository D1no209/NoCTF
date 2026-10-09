<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdTeamsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTeamsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdTeamsPageViewState }>()
const { appealsOnly, adminUserPath, teamDetailOpen, teamLoading, teamDetailError, competitionId, canJudge, canWrite, teams, search, loading, pageLoading, error, page, pageCount, total, pageLimit, loadPage, setPageSize, pendingId, tracks, tracksEnabled, registrationStatusOptions, selectedTeam, teamMembers, teamDetailLoading, teamInvitationToken, teamInvitationLoading, teamInvitationError, reloadTeamInvitation, copyTeamInvitation, expandedMemberId, setExpandedMember, teamDisplayNames, displayTeamName, scoreAdjustmentTeam, scoreAdjustmentChallenges, scoreAdjustmentChallengeId, scoreAdjustmentDelta, scoreAdjustmentLoading, scoreAdjustmentPending, scoreAdjustmentError, scoreAdjustmentValid, openScoreAdjustment, closeScoreAdjustment, submitScoreAdjustment, openTeamDetail, assignTrackValue, setRegistrationStatusValue, banDialog, banReason, banAnnouncePublicly, banPending, banReasonValid, openBan, submitBan, appeals, appealsLoading, appealsError, appealDialog, appealReason, appealPending, openAppeal, submitAppeal, PrivateAccountPanel, onUpdateOpenOpen, onClickScoreAdjustmentTeam, onUpdateOpenBanDialog, onClickBanDialog, onUpdateOpenAppealDialog, onClickAppealDialog } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-8">
    <div v-if="!appealsOnly" class="flex flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('administration.label.teamManagement') }}</h2>
      <Input v-model="search" :placeholder="$t('common.label.searchTeam')" :aria-label="$t('common.label.searchTeam')" />
      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ $message(error) }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading" class="h-48 w-full" />
      <Empty v-else-if="teams.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('administration.competitionsBy.description.thereRegisteredTeamYet') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('administration.label.teamName') }}</TableHead>
            <TableHead v-if="tracksEnabled" class="min-w-36">{{ $t('common.label.tracks') }}</TableHead>
            <TableHead class="w-24">{{ $t('administration.label.numberPeople') }}</TableHead>
            <TableHead class="w-28">{{ $t('administration.label.registrationStatus') }}</TableHead>
            <TableHead class="w-28">{{ $t('administration.label.banStatus') }}</TableHead>
            <TableHead class="w-44">{{ $t('administration.label.registrationTime') }}</TableHead>
            <TableHead v-if="canWrite || canJudge" class="w-64 text-right">{{ $t('common.label.actions') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in teams" :key="t.id">
            <TableCell>
              <ActionButton
                type="button"
                class="w-max max-w-64 whitespace-normal rounded-sm text-left font-medium underline-offset-4 [overflow-wrap:anywhere] hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                @click="openTeamDetail(t)"
              >
                {{ displayTeamName(t) }}
              </ActionButton>
            </TableCell>
            <TableCell v-if="tracksEnabled">
              <Select
                v-if="canWrite"
                :model-value="t.trackKey"
                :disabled="pendingId === t.id"
                @update:model-value="assignTrackValue(t, $event)"
              >
                <SelectTrigger class="min-w-32"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="track in tracks" :key="track.key" :value="track.key!">
                    {{ track.name }}<template v-if="track.isInternal"> · {{ $t('administration.label.internal') }}</template>
                  </SelectItem>
                </SelectContent>
              </Select>
              <Badge v-else variant="outline" class="max-w-64 whitespace-normal text-left [overflow-wrap:anywhere]">{{ t.trackName ?? t.trackKey }}</Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ t.memberIds?.length ?? 0 }}</TableCell>
            <TableCell>
              <DropdownMenu v-if="canWrite">
                <DropdownMenuTrigger as-child>
                  <ActionButton type="button" :disabled="pendingId === t.id" :aria-label="$t('administration.label.changeTeamRegistrationStatus', { team: displayTeamName(t) })">
                    <Badge :variant="t.registrationStatus === 'Approved' ? 'default' : t.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                      {{ enumLabel(TeamRegistrationStatusLabel, t.registrationStatus) }}
                    </Badge>
                  </ActionButton>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start">
                  <DropdownMenuItem
                    v-for="option in registrationStatusOptions"
                    :key="option.value"
                    :disabled="pendingId === t.id || option.value === t.registrationStatus"
                    @select="setRegistrationStatusValue(t, option.value)"
                  >
                    {{ $t(option.label) }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
              <Badge v-else :variant="t.registrationStatus === 'Approved' ? 'default' : t.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                {{ enumLabel(TeamRegistrationStatusLabel, t.registrationStatus) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="t.isBanned" variant="destructive">{{ $t('common.label.banned') }}</Badge>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(t.registeredAt) }}</TableCell>
            <TableCell v-if="canWrite || canJudge" class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button v-if="canJudge" variant="outline" size="sm" :disabled="pendingId === t.id" @click="openScoreAdjustment(t)">
                  {{ $t('administration.label.adjustScore') }}
                </Button>
                <template v-if="canJudge && !t.isBanned">
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="openBan(t, 'ban')">{{ $t('administration.label.ban') }}</Button>
                </template>
                <template v-else-if="canWrite">
                  <Button type="button" variant="ghost" size="sm" :disabled="pendingId === t.id" @click.stop="openBan(t, 'correct')">{{ $t('administration.label.correctBan') }}</Button>
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

    <Sheet :open="teamDetailOpen" @update:open="onUpdateOpenOpen">
      <SheetContent data-scroll-surface class="overflow-y-auto sm:max-w-lg">
        <SheetHeader class="px-6 py-5 pr-14">
          <SheetTitle>{{ $t('common.label.teamDetails') }}</SheetTitle>
          <SheetDescription class="[overflow-wrap:anywhere]">{{ selectedTeam ? displayTeamName(selectedTeam) : '' }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="teamLoading" class="mx-6 h-48" />
        <Alert v-else-if="teamDetailError" variant="destructive"><AlertDescription>{{ $message(teamDetailError) }}</AlertDescription></Alert>
        <div v-else-if="selectedTeam" class="flex flex-col gap-6 px-6 pb-6">
          <dl class="grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
            <dt v-if="tracksEnabled" class="text-muted-foreground">{{ $t('common.label.tracks') }}</dt>
            <dd v-if="tracksEnabled">{{ selectedTeam.trackName ?? selectedTeam.trackKey }}</dd>
            <dt class="text-muted-foreground">{{ $t('administration.label.registrationStatus') }}</dt>
            <dd>{{ enumLabel(TeamRegistrationStatusLabel, selectedTeam.registrationStatus) }}</dd>
            <dt class="text-muted-foreground">{{ $t('administration.label.banStatus') }}</dt>
            <dd>{{ selectedTeam.isBanned ? $t('common.label.banned') : $t('administration.label.normal') }}</dd>
            <dt class="text-muted-foreground">{{ $t('administration.label.registrationTime') }}</dt>
            <dd class="font-mono tabular-nums">{{ adminFormatDateTime(selectedTeam.registeredAt) }}</dd>
            <dt v-if="canWrite" class="text-muted-foreground">{{ $t('competitions.label.invitationCode') }}</dt>
            <dd v-if="canWrite" class="min-w-0">
              <Skeleton v-if="teamInvitationLoading" class="h-10 w-full" />
              <div v-else-if="teamInvitationError" class="flex flex-wrap items-center gap-2">
                <span role="alert" class="text-sm text-destructive">{{ $message(teamInvitationError) }}</span>
                <Button type="button" size="sm" variant="outline" @click="reloadTeamInvitation">
                  {{ $t('common.label.retry') }}
                </Button>
              </div>
              <div v-else-if="teamInvitationToken" class="flex min-w-0 items-center gap-2">
                <Input :model-value="teamInvitationToken" readonly class="min-w-0 font-mono" />
                <Button type="button" size="sm" variant="outline" @click="copyTeamInvitation">
                  {{ $t('common.action.copy') }}
                </Button>
              </div>
              <span v-else class="text-muted-foreground">-</span>
            </dd>
          </dl>
          <Separator />
          <section class="flex flex-col gap-3">
            <h3 class="font-semibold">{{ $t('common.label.members.teamPageView', { count: selectedTeam.memberIds?.length ?? 0 }) }}</h3>
            <div v-if="teamDetailLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
              <Spinner class="size-4" />{{ $t('administration.label.loading') }}
            </div>
            <Accordion
              v-else
              type="single"
              collapsible
              :model-value="expandedMemberId ?? undefined"
              class="rounded-md bg-muted/20"
              @update:model-value="setExpandedMember"
            >
              <AccordionItem v-for="member in teamMembers" :key="member.userId" :value="member.userId!" class="px-3">
                <div class="flex flex-wrap items-center gap-3 py-3">
                  <Avatar class="size-9">
                    <AvatarImage v-if="member.avatarUrl" :src="member.avatarUrl" :alt="member.userName ?? ''" />
                    <AvatarFallback>{{ member.userName?.slice(0, 2) }}</AvatarFallback>
                  </Avatar>
                  <div class="min-w-0 flex-1">
                    <NuxtLink :to="adminUserPath(member.userId)" class="font-medium hover:underline">
                      {{ member.userName }}
                    </NuxtLink>
                    <NuxtLink :to="`/users/${member.userId}`" class="ml-3 text-xs text-muted-foreground hover:underline">{{ $t('adminNavigation.publicProfile') }}</NuxtLink>
                  </div>
                  <Badge v-if="member.userId === selectedTeam.captainId" variant="secondary">{{ $t('common.label.captain') }}</Badge>
                </div>
                <AccordionTrigger v-if="canJudge">{{ $t('administration.label.privateMemberDetails') }}</AccordionTrigger>
                <AccordionContent v-if="canJudge" class="pb-3">
                  <component
                    :is="PrivateAccountPanel"
                    v-if="expandedMemberId === member.userId && member.userId && selectedTeam.id"
                    :user-id="member.userId"
                    :competition-id="competitionId"
                    :team-id="selectedTeam.id"
                    :show-activities="false"
                  />
                </AccordionContent>
              </AccordionItem>
              <p v-if="!teamMembers.length" class="p-3 text-sm text-muted-foreground">{{ $t('administration.label.members') }}</p>
            </Accordion>
          </section>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="scoreAdjustmentTeam !== null" @update:open="closeScoreAdjustment">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('administration.label.scoreAdjustment') }}</DialogTitle>
          <DialogDescription class="[overflow-wrap:anywhere]">
            {{ $t('administration.competitionsBy.description.recordChallengeScoreAdjustment', { team: scoreAdjustmentTeam ? displayTeamName(scoreAdjustmentTeam) : '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="scoreAdjustmentError" variant="destructive">
            <AlertDescription>{{ $message(scoreAdjustmentError) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="score-adjustment-challenge">{{ $t('common.label.challenge.pageTitle') }}</FieldLabel>
            <Skeleton v-if="scoreAdjustmentLoading" class="h-10 w-full" />
            <Select v-else v-model="scoreAdjustmentChallengeId">
              <SelectTrigger id="score-adjustment-challenge" class="w-full">
                <SelectValue :placeholder="$t('administration.label.selectChallenge')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="challenge in scoreAdjustmentChallenges" :key="challenge.id" :value="challenge.id!">
                  {{ challenge.title }} · {{ challenge.direction || '' }}
                </SelectItem>
              </SelectContent>
            </Select>
            <FieldDescription v-if="!scoreAdjustmentLoading && scoreAdjustmentChallenges.length === 0">
              {{ $t('administration.competitionsBy.description.competitionChallengesAvailableAdjustment') }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="score-adjustment-delta">{{ $t('administration.label.scoreDelta') }}</FieldLabel>
            <NumberInput id="score-adjustment-delta" v-model.number="scoreAdjustmentDelta"  step="1" />
            <FieldDescription>{{ $t('administration.competitionsBy.description.enterNonZeroInteger') }}</FieldDescription>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="scoreAdjustmentPending" @click="onClickScoreAdjustmentTeam(null)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="scoreAdjustmentLoading || scoreAdjustmentPending || !scoreAdjustmentValid" @click="submitScoreAdjustment">
            <Spinner v-if="scoreAdjustmentPending" data-icon="inline-start" />
            {{ $t('administration.label.confirmAdjustment') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Separator v-if="!appealsOnly" />

    <div id="ban-appeals" class="flex scroll-mt-24 flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('common.label.banAppeal') }}</h2>
      <Alert v-if="appealsError" variant="destructive">
        <AlertDescription>{{ $message(appealsError) }}</AlertDescription>
      </Alert>
      <Skeleton v-if="appealsLoading" class="h-32 w-full" />
      <Empty v-else-if="!appealsError && appeals.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('administration.label.appealYet') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <div v-else-if="appeals.length > 0" class="flex flex-col gap-3">
        <Card v-for="a in appeals" :id="`appeal-${a.appeal?.id ?? a.banEventId}`" :key="a.banEventId" class="scroll-mt-24">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <CardTitle class="min-w-0 text-base [overflow-wrap:anywhere]">{{ teamDisplayName(a, teamDisplayNames) }}</CardTitle>
              <div class="flex items-center gap-1">
                <Badge variant="outline">{{ enumLabel(TeamBanSourceLabel, a.source) }}</Badge>
                <Badge v-if="a.appeal" :variant="a.appeal.status === 'Submitted' ? 'secondary' : a.appeal.status === 'Accepted' ? 'default' : 'destructive'">
                  {{ $t('administration.label.appeal', { status: enumLabel(TeamBanAppealStatusLabel, a.appeal.status) }) }}
                </Badge>
                <Badge v-if="a.isCurrentlyBanned" variant="destructive">{{ $t('administration.label.banned') }}</Badge>
              </div>
            </div>
            <CardDescription>{{ $t('administration.label.banned.teamsPageView', { time: adminFormatDateTime(a.bannedAt) }) }}</CardDescription>
          </CardHeader>
          <CardContent v-if="a.appeal" class="flex flex-col gap-2 text-sm">
            <p><span class="text-muted-foreground">{{ $t('administration.label.complainant') }}</span>{{ a.appeal.submittedByUserName }} · {{ adminFormatDateTime(a.appeal.submittedAt) }}</p>
            <p class="whitespace-pre-wrap">{{ a.appeal.statement }}</p>
            <p v-if="a.appeal.resolutionReason" class="text-muted-foreground">
              {{ $t('administration.label.resolved', { user: a.appeal.resolvedByUserName ?? '-', time: adminFormatDateTime(a.appeal.resolvedAt), reason: a.appeal.resolutionReason }) }}
            </p>
          </CardContent>
          <CardFooter v-if="canJudge && a.appeal?.status === 'Submitted' && a.canResolve" class="gap-2">
            <Button size="sm" :disabled="appealPending" @click="openAppeal(a, 'accept')">{{ $t('administration.label.acceptAppealUnblock') }}</Button>
            <Button variant="outline" size="sm" :disabled="appealPending" @click="openAppeal(a, 'uphold')">{{ $t('administration.label.maintainBan') }}</Button>
          </CardFooter>
        </Card>
      </div>
    </div>

    <Dialog :open="banDialog !== null" @update:open="onUpdateOpenBanDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ banDialog?.mode === 'ban' ? $t('administration.label.banTeam') : $t('administration.label.correctBan') }}</DialogTitle>
          <DialogDescription class="[overflow-wrap:anywhere]">
            {{ banDialog?.mode === 'ban'
              ? $t('administration.competitionsBy.description.banTeamLongerAble', { team: banDialog?.team.name ?? '-' })
              : $t('administration.competitionsBy.error.markBanPublishFailed', { team: banDialog?.team.name ?? '-' }) }}
            {{ $t('administration.competitionsBy.validation.reasonRequired') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="ban-reason">{{ $t('administration.label.reason') }}</FieldLabel>
            <Textarea id="ban-reason" v-model="banReason" maxlength="512" required />
            <FieldDescription v-if="banDialog?.mode === 'correct'">
              {{ $t('administration.label.leastCharactersCurrently', { length: banReason.trim().length }) }}
            </FieldDescription>
            <FieldDescription v-else>
              {{ $t('administration.label.currently', { length: banReason.trim().length }) }}
            </FieldDescription>
          </Field>
          <Field v-if="banDialog?.mode === 'ban'" orientation="horizontal">
            <Checkbox id="ban-announce-publicly" v-model="banAnnouncePublicly" aria-describedby="ban-announce-publicly-description" />
            <FieldContent class="min-w-0">
              <FieldLabel for="ban-announce-publicly">{{ $t('administration.competitionsBy.description.releaseEventDisciplineAnnouncement') }}</FieldLabel>
              <FieldDescription id="ban-announce-publicly-description">{{ $t('administration.competitionsBy.description.offDefaultOpeningParticipants') }}</FieldDescription>
            </FieldContent>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickBanDialog(null)">{{ $t('common.action.cancel') }}</Button>
          <Button type="button" :disabled="banPending || !banReasonValid" @click="submitBan">
            <Spinner v-if="banPending" data-icon="inline-start" /> {{ $t('common.label.confirm') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="appealDialog !== null" @update:open="onUpdateOpenAppealDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ appealDialog?.mode === 'accept' ? $t('administration.label.acceptAppeal') : $t('administration.label.maintainBan') }}</DialogTitle>
          <DialogDescription class="[overflow-wrap:anywhere]">
            {{ appealDialog?.mode === 'accept'
              ? $t('administration.competitionsBy.description.acceptSAppealLift', { team: appealDialog?.banCase.teamName ?? '-' })
              : $t('administration.competitionsBy.description.rejectSAppealUphold', { team: appealDialog?.banCase.teamName ?? '-' }) }}
            {{ $t('administration.competitionsBy.validation.resolutionReasonRequired') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="appeal-reason">{{ $t('administration.label.reasonsRuling') }}</FieldLabel>
            <Textarea id="appeal-reason" v-model="appealReason" required />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickAppealDialog(null)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="appealPending || !appealReason.trim()" @click="submitAppeal">
            <Spinner v-if="appealPending" data-icon="inline-start" /> {{ $t('common.label.confirm') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
