<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsNewPageViewState } from '~/features/routes/admin/competitions/useAdminCompetitionsNewPage'

const viewProps = defineProps<{ state: AdminCompetitionsNewPageViewState }>()
const { canOrganize, title, description, mode, startTime, endTime, teamRegistrationAutoApprove, allowTeamRegistrationWhileRunning, maxTeamMembers, maxConcurrentRuntimeInstancesPerTeam, maxActiveQuestionsPerTeam, maxParticipantMessagesBeforeHandlerReply, allowChallengeOwnersToHandleQuestions, practiceModeEnabled, error, pending, submit } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-2xl flex-col px-4 py-8">
    <Alert v-if="!canOrganize" variant="destructive" class="mb-4">
      <AlertDescription>{{ $t('ui.theCurrentAccountDoesNotHavePermissionToCreateCompetitions') }}</AlertDescription>
    </Alert>
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.newCompetition') }}</CardTitle>
        <CardDescription>{{ $t('ui.theGameModeCannotBeModifiedAfterCreationAndThe') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="title">{{ $t('ui.title') }}</FieldLabel>
              <Input id="title" v-model="title" required maxlength="200" />
            </Field>
            <Field>
              <FieldLabel for="description">{{ $t('ui.description') }}</FieldLabel>
              <Textarea id="description" v-model="description" :placeholder="$t('ui.optional')" />
            </Field>
            <Field>
              <FieldLabel for="mode">{{ $t('ui.gameMode') }}</FieldLabel>
              <Select id="mode" v-model="mode">
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="$t('ui.selectMode')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="Ctf">{{ $t('ui.ctf') }}</SelectItem>
                    <SelectItem value="Awd">{{ $t('ui.awd') }}</SelectItem>
                    <SelectItem value="Awdp">{{ $t('ui.awdp') }}</SelectItem>
                    <SelectItem value="Koh">{{ $t('ui.koh') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
              <FieldDescription>{{ $t('ui.cannotBeModifiedAfterCreation') }}</FieldDescription>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="start">{{ $t('ui.startTime') }}</FieldLabel>
                <Input id="start" v-model="startTime" type="datetime-local" required />
              </Field>
              <Field>
                <FieldLabel for="end">{{ $t('ui.endTime') }}</FieldLabel>
                <Input id="end" v-model="endTime" type="datetime-local" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="max-members">{{ $t('ui.maximumNumberOfPeoplePerTeam') }}</FieldLabel>
                <Input id="max-members" v-model.number="maxTeamMembers" type="number" min="1" required />
              </Field>
              <Field>
                <FieldLabel for="max-runtime">{{ $t('ui.maximumConcurrentRuntimePerTeam') }}</FieldLabel>
                <Input id="max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="auto-approve" v-model="teamRegistrationAutoApprove" />
              <FieldLabel for="auto-approve" class="font-normal">{{ $t('ui.teamRegistrationIsAutomaticallyPassedNoApprovalRequired') }}</FieldLabel>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="allow-running-registration" v-model="allowTeamRegistrationWhileRunning" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="allow-running-registration" class="font-normal">{{ $t('ui.teamCreationIsStillAllowedDuringTheGame') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.whenClosedTheCompetitionWillStopAcceptingNewTeamsAnd') }}</FieldDescription>
              </div>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="max-active-questions">{{ $t('ui.activeConsultationLimitPerTeam') }}</FieldLabel>
                <Input id="max-active-questions" v-model.number="maxActiveQuestionsPerTeam" type="number" min="1" required />
                <FieldDescription>{{ $t('ui.pendingAndRespondedInquiriesCountTowardsTheCap') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="max-participant-messages">{{ $t('ui.maximumNumberOfContinuousSupplementaryMessages') }}</FieldLabel>
                <Input id="max-participant-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" type="number" min="1" required />
                <FieldDescription>{{ $t('ui.initialQuestionsAreIncludedInTheQuotaTheyWillBe') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="allow-challenge-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="allow-challenge-owner-questions" class="font-normal">{{ $t('ui.allowQuestionOwnersToHandleRelatedInquiries') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.onlyForTopicsItOwnsOrCollaboratesOnNoFlag') }}</FieldDescription>
              </div>
            </Field>
            <Field v-if="mode === 'Ctf'" orientation="horizontal">
              <Checkbox id="practice-mode" v-model="practiceModeEnabled" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="practice-mode" class="font-normal">{{ $t('ui.enablePracticeModeAfterTheCompetition') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.approvedNonBannedCompetitionTeamsCanStartContainerChallengesAnd') }}</FieldDescription>
              </div>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending || !canOrganize" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.createContest') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
