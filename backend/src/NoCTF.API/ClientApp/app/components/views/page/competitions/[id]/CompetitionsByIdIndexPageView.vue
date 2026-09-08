<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdIndexPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdIndexPage'

const viewProps = defineProps<{ state: CompetitionsByIdIndexPageViewState }>()
const { ArrowRight, Box, CalendarRange, Clock, FileText, KeyRound, LogIn, ShieldCheck, Trophy, UserPlus, Users, competitionId, isLoggedIn, competition, myTeam, teamLoaded, teamLoadError, loadMyTeam, approvedTeamCount, selectableTracks, tracksLoaded, trackLoadError, loadRegistrationOptions, countdown, practiceOpen, canParticipate, teamRegistrationOpen, createOpen, createName, createTrackKey, createTrackInvitationCode, createPending, createValidationError, selectedCreateTrack, submitCreate, joinOpen, joinToken, joinPending, joinValidationError, submitJoin, isCaptain, LifecycleBadge, ModeBadge } = toRefs(viewProps.state)
</script>

<template>
  <div v-if="competition" class="flex flex-col gap-6">
    <!-- Hero -->
    <Card class="overflow-hidden">
      <div class="grid gap-8 p-6 sm:p-8 lg:grid-cols-[minmax(0,1fr)_17rem] lg:gap-12">
        <div class="flex min-w-0 flex-col gap-5">
          <div class="flex flex-wrap items-center gap-2">
            <component :is="ModeBadge" :mode="competition.mode" />
            <component :is="LifecycleBadge" :status="competition.status" />
          </div>

          <h1 class="text-display text-3xl sm:text-4xl">{{ competition.title }}</h1>

          <p class="flex items-center gap-2 font-mono text-xs text-muted-foreground tabular-nums sm:text-sm">
            <CalendarRange class="size-4 shrink-0" />
            {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
          </p>

          <div v-if="countdown" class="flex flex-wrap items-end gap-x-3 gap-y-1">
            <span v-if="countdown.ms > 0" class="flex items-center gap-1.5 pb-2 text-sm text-muted-foreground">
              <Clock class="size-4" />
              {{ countdown.label }}
            </span>
            <span class="font-mono text-3xl font-semibold text-primary tabular-nums sm:text-4xl">
              {{ countdown.ms > 0 ? formatDuration(countdown.ms) : countdown.label }}
            </span>
          </div>

          <p v-if="practiceOpen" class="text-sm text-muted-foreground">
            {{ $t('ui.postCompetitionPracticeIsOpenToApprovedNonBannedParticipating') }}
          </p>

          <div class="flex flex-wrap items-center gap-3 pt-1">
            <template v-if="!teamLoaded">
              <Skeleton class="h-10 w-32" />
            </template>

            <template v-else-if="!isLoggedIn">
              <Button as-child size="lg">
                <NuxtLink :to="`/auth/login?redirect=/competitions/${competitionId}`">
                  <LogIn data-icon="inline-start" /> {{ practiceOpen ? $t('ui.signInToPractice') : $t('ui.signUpAfterLoginRegister') }} </NuxtLink>
              </Button>
            </template>

            <template v-else-if="teamLoadError">
              <Alert variant="destructive" class="w-auto">
                <AlertDescription class="flex items-center gap-3">
                  <span>{{ $message(teamLoadError) }}</span>
                  <Button type="button" size="sm" variant="outline" @click="loadMyTeam">
                    {{ $t('ui.retry') }}
                  </Button>
                </AlertDescription>
              </Alert>
            </template>

            <template v-else-if="!myTeam">
              <Dialog v-if="teamRegistrationOpen" v-model:open="createOpen">
                <DialogTrigger as-child>
                  <Button size="lg">
                    <UserPlus data-icon="inline-start" /> {{ $t('ui.signUpNow') }} </Button>
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>{{ $t('ui.createATeam') }}</DialogTitle>
                    <DialogDescription>{{ $t('ui.afterTheTeamIsCreatedYouBecomeTheLeaderAnd') }}</DialogDescription>
                  </DialogHeader>
                  <form novalidate @submit.prevent="submitCreate">
                    <FieldGroup>
                      <Field>
                        <FieldLabel for="team-name">{{ $t('ui.teamName2') }}</FieldLabel>
                        <Input id="team-name" v-model="createName" required maxlength="64" />
                      </Field>
                      <Field v-if="!tracksLoaded">
                        <Skeleton class="h-10 w-full" />
                        <FieldDescription>{{ $t('ui.loadingAvailableCompetitionTracks') }}</FieldDescription>
                      </Field>
                      <Field v-else-if="trackLoadError">
                        <Alert variant="destructive">
                          <AlertDescription class="flex items-center justify-between gap-3">
                            <span>{{ $message(trackLoadError) }}</span>
                            <Button type="button" size="sm" variant="outline" @click="loadRegistrationOptions">
                              {{ $t('ui.retry') }}
                            </Button>
                          </AlertDescription>
                        </Alert>
                      </Field>
                      <Field v-else-if="selectableTracks.length > 0">
                        <FieldLabel for="team-track">{{ $t('ui.competitionTrack') }}</FieldLabel>
                        <Select v-model="createTrackKey" required>
                          <SelectTrigger id="team-track"><SelectValue :placeholder="$t('ui.selectATrack')" /></SelectTrigger>
                          <SelectContent>
                            <SelectItem v-for="track in selectableTracks" :key="track.key" :value="track.key!">
                              {{ track.name }}
                            </SelectItem>
                          </SelectContent>
                        </Select>
                        <FieldDescription>{{ $t('ui.selectTheTrackThisTeamWillEnterCompetitionAdministratorsCan') }}</FieldDescription>
                      </Field>
                      <Field v-else>
                        <Alert variant="destructive">
                          <AlertDescription>{{ $t('ui.noCompetitionTracksAreCurrentlyOpenForRegistration') }}</AlertDescription>
                        </Alert>
                      </Field>
                      <Field v-if="selectedCreateTrack?.requiresInvitationCode">
                        <FieldLabel for="track-invitation-code">{{ $t('ui.trackInvitationCode') }}</FieldLabel>
                        <Input
                          id="track-invitation-code"
                          v-model="createTrackInvitationCode"
                          type="password"
                          required
                          autocomplete="off"
                          maxlength="128"
                        />
                      </Field>
                      <p v-if="createValidationError" role="alert" class="text-sm text-destructive">
                        {{ $message(createValidationError) }}
                      </p>
                      <Field>
                        <Button
                          type="submit"
                          class="w-full"
                          :disabled="createPending || !tracksLoaded || Boolean(trackLoadError) || selectableTracks.length === 0"
                        >
                          <Spinner v-if="createPending" data-icon="inline-start" /> {{ $t('ui.create') }} </Button>
                      </Field>
                    </FieldGroup>
                  </form>
                </DialogContent>
              </Dialog>

              <Alert v-else class="w-auto">
                <AlertDescription>{{ $t('ui.newTeamCreationIsClosedForTheCurrentCompetitionStage') }}</AlertDescription>
              </Alert>

              <Dialog v-model:open="joinOpen">
                <DialogTrigger as-child>
                  <Button size="lg" variant="outline">
                    <KeyRound data-icon="inline-start" /> {{ $t('ui.joinWithInvitationCode') }} </Button>
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>{{ $t('ui.joinTheTeam') }}</DialogTitle>
                    <DialogDescription>{{ $t('ui.enterThe32DigitInvitationCodeSharedWithYouBy') }}</DialogDescription>
                  </DialogHeader>
                  <form @submit.prevent="submitJoin">
                    <FieldGroup>
                      <Field>
                        <FieldLabel for="invitation-token">{{ $t('ui.invitationCode') }}</FieldLabel>
                        <Input id="invitation-token" v-model="joinToken" required minlength="32" maxlength="32" autocomplete="off" />
                      </Field>
                      <p v-if="joinValidationError" role="alert" class="text-sm text-destructive">{{ $message(joinValidationError) }}</p>
                      <Field>
                        <Button type="submit" class="w-full" :disabled="joinPending">
                          <Spinner v-if="joinPending" data-icon="inline-start" /> {{ $t('ui.join') }} </Button>
                      </Field>
                    </FieldGroup>
                  </form>
                </DialogContent>
              </Dialog>
            </template>

            <template v-else>
              <Button v-if="canParticipate" as-child size="lg">
                <NuxtLink :to="`/competitions/${competitionId}/challenges`"> {{ practiceOpen ? $t('ui.enterPractice') : $t('ui.enterTheCompetition') }} <ArrowRight data-icon="inline-end" />
                </NuxtLink>
              </Button>
              <Button as-child size="lg" variant="outline">
                <NuxtLink :to="`/competitions/${competitionId}/my/team`">{{ $t('ui.myTeam') }}</NuxtLink>
              </Button>
            </template>
          </div>
        </div>

        <dl class="flex flex-col justify-center gap-4 lg:border-l lg:pl-10">
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Users class="size-3.5" />
              {{ $t('ui.teamSizeLimit2') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('ui.members4', { count: competition.maxTeamMembers ?? '-' }) }}
            </dd>
          </div>
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Box class="size-3.5" />
              {{ $t('ui.concurrentEnvironments') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('ui.message2', { count: competition.maxConcurrentRuntimeInstancesPerTeam ?? '-' }) }}
            </dd>
          </div>
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <ShieldCheck class="size-3.5" />
              {{ $t('ui.approval') }}
            </dt>
            <dd class="text-sm">
              {{ competition.teamRegistrationAutoApprove ? $t('ui.registrationAutomaticallyPasses') : $t('ui.registrationIsSubjectToReview') }}
            </dd>
          </div>
          <div v-if="approvedTeamCount !== null" class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Trophy class="size-3.5" />
              {{ $t('ui.registeredTeams') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('ui.teams3', { count: approvedTeamCount }) }}
            </dd>
          </div>
        </dl>
      </div>
    </Card>

    <!-- 我的队伍状态提示 -->
    <Alert v-if="myTeam && myTeam.registrationStatus === 'Pending'">
      <ShieldCheck class="size-4" />
      <AlertDescription>
        {{ $t('ui.teamSRegistrationWasSubmittedYouCanCompeteAfterOrganizer', { team: myTeam.name ?? '-' }) }}
      </AlertDescription>
    </Alert>
    <Alert v-else-if="myTeam && myTeam.registrationStatus === 'Rejected'" variant="destructive">
      <AlertDescription>
        {{ $t('ui.message11') }}{{ myTeam.name }}{{ $t('ui.message12') }}{{ isCaptain ? $t('ui.youCanModifyTheInformationOnTheMyTeamPage') : '' }}。
      </AlertDescription>
    </Alert>
    <Alert v-else-if="myTeam?.isBanned" variant="destructive">
      <AlertDescription>{{ $t('ui.teamHasBeenBannedContactTheOrganizersIfYouWant', { team: myTeam.name ?? '-' }) }}</AlertDescription>
    </Alert>
    <Alert v-if="myTeam">
      <AlertDescription>{{ $t('ui.currentTrack', { track: myTeam.trackName ?? myTeam.trackKey ?? '-' }) }}</AlertDescription>
    </Alert>

    <!-- 竞赛介绍 -->
    <Card>
      <CardHeader>
        <CardTitle class="flex items-center gap-2 text-base">
          <FileText class="size-4" /> {{ $t('ui.competitionIntroduction') }} </CardTitle>
      </CardHeader>
      <CardContent>
        <p v-if="competition.description" class="whitespace-pre-line text-sm leading-7">
          {{ competition.description }}
        </p>
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.theOrganizersHaveNotYetFilledOutTheCompetitionDescription') }}</p>
      </CardContent>
    </Card>
  </div>
</template>
