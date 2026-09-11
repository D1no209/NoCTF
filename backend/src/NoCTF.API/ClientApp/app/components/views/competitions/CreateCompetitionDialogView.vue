<script setup lang="ts">
import { toRefs } from 'vue'
import type { CreateCompetitionDialogViewState } from '~/features/competitions/useCreateCompetitionDialog'

const viewProps = defineProps<{ state: CreateCompetitionDialogViewState }>()
const { open, canOrganize, title, description, mode, startTime, endTime, teamRegistrationAutoApprove, allowTeamRegistrationWhileRunning, maxTeamMembers, maxConcurrentRuntimeInstancesPerTeam, maxActiveQuestionsPerTeam, maxParticipantMessagesBeforeHandlerReply, allowChallengeOwnersToHandleQuestions, practiceModeEnabled, posterError, posterInputKey, submitLabel, error, pending, selectPoster, setOpen, submit } = toRefs(viewProps.state)
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col p-0 sm:max-w-3xl">
      <DialogHeader class="px-6 pt-6">
        <DialogTitle>{{ $t('ui.newCompetition') }}</DialogTitle>
        <DialogDescription>{{ $t('ui.theGameModeCannotBeModifiedAfterCreationAndThe') }}</DialogDescription>
      </DialogHeader>

      <Alert v-if="!canOrganize" variant="destructive" class="mx-6">
        <AlertDescription>{{ $t('ui.theCurrentAccountDoesNotHavePermissionToCreateCompetitions') }}</AlertDescription>
      </Alert>

      <UiForm class="flex min-h-0 flex-1 flex-col gap-5" @submit.prevent="submit">
        <ScrollSurface axis="y" class="min-h-0 flex-1 overscroll-contain" :aria-label="$t('ui.newCompetition')">
          <FieldGroup class="px-6 pb-1">
          <Alert v-if="error" variant="destructive">
            <AlertDescription>{{ $message(error) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="create-competition-title">{{ $t('ui.title') }}</FieldLabel>
            <Input id="create-competition-title" v-model="title" required maxlength="200" />
          </Field>
          <Field>
            <FieldLabel for="create-competition-description">{{ $t('ui.description') }}</FieldLabel>
            <Textarea id="create-competition-description" v-model="description" :placeholder="$t('ui.optional')" />
          </Field>
          <Field>
            <FieldLabel for="create-competition-mode">{{ $t('ui.gameMode') }}</FieldLabel>
            <Select id="create-competition-mode" v-model="mode">
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
              <FieldLabel for="create-competition-start">{{ $t('ui.startTime') }}</FieldLabel>
              <DateTimePicker id="create-competition-start" v-model="startTime" required />
            </Field>
            <Field>
              <FieldLabel for="create-competition-end">{{ $t('ui.endTime') }}</FieldLabel>
              <DateTimePicker id="create-competition-end" v-model="endTime" required />
            </Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="create-competition-max-members">{{ $t('ui.maximumNumberOfPeoplePerTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-members" v-model.number="maxTeamMembers" min="1" required />
            </Field>
            <Field>
              <FieldLabel for="create-competition-max-runtime">{{ $t('ui.maximumConcurrentRuntimePerTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" min="1" required />
            </Field>
          </div>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-auto-approve" v-model="teamRegistrationAutoApprove" />
            <FieldLabel for="create-competition-auto-approve" class="font-normal">{{ $t('ui.teamRegistrationIsAutomaticallyPassedNoApprovalRequired') }}</FieldLabel>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-running-registration" v-model="allowTeamRegistrationWhileRunning" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-running-registration" class="font-normal">{{ $t('ui.teamCreationIsStillAllowedDuringTheGame') }}</FieldLabel>
              <FieldDescription>{{ $t('ui.whenClosedTheCompetitionWillStopAcceptingNewTeamsAnd') }}</FieldDescription>
            </div>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="create-competition-max-questions">{{ $t('ui.activeConsultationLimitPerTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-questions" v-model.number="maxActiveQuestionsPerTeam" min="1" required />
              <FieldDescription>{{ $t('ui.pendingAndRespondedInquiriesCountTowardsTheCap') }}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="create-competition-max-messages">{{ $t('ui.maximumNumberOfContinuousSupplementaryMessages') }}</FieldLabel>
              <NumberInput id="create-competition-max-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" min="1" required />
              <FieldDescription>{{ $t('ui.initialQuestionsAreIncludedInTheQuotaTheyWillBe') }}</FieldDescription>
            </Field>
          </div>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-owner-questions" class="font-normal">{{ $t('ui.allowQuestionOwnersToHandleRelatedInquiries') }}</FieldLabel>
              <FieldDescription>{{ $t('ui.onlyForTopicsItOwnsOrCollaboratesOnNoFlag') }}</FieldDescription>
            </div>
          </Field>
          <Field v-if="mode === 'Ctf'" orientation="horizontal">
            <Checkbox id="create-competition-practice-mode" v-model="practiceModeEnabled" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-practice-mode" class="font-normal">{{ $t('ui.enablePracticeModeAfterTheCompetition') }}</FieldLabel>
              <FieldDescription>{{ $t('ui.approvedNonBannedCompetitionTeamsCanStartContainerChallengesAnd') }}</FieldDescription>
            </div>
          </Field>
          <Field>
            <FieldLabel for="create-competition-poster">{{ $t('ui.competitionPoster') }}</FieldLabel>
            <FileUpload
              :key="posterInputKey"
              id="create-competition-poster"
              accept="image/jpeg,image/png,image/webp"
              :disabled="pending"
              :error="posterError"
              @change="selectPoster"
            />
            <FieldDescription>{{ $t('createCompetition.posterDescription') }}</FieldDescription>
          </Field>
          </FieldGroup>
        </ScrollSurface>

        <DialogFooter class="m-0 shrink-0 rounded-b-xl px-6 py-4">
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button type="submit" :disabled="pending || !canOrganize || Boolean(posterError)">
            <Spinner v-if="pending" data-icon="inline-start" />{{ submitLabel }}
          </Button>
        </DialogFooter>
      </UiForm>
    </DialogContent>
  </Dialog>
</template>
