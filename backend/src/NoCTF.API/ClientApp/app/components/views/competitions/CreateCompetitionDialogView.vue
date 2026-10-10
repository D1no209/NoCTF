<script setup lang="ts">
import { toRefs } from 'vue'
import type { CreateCompetitionDialogViewState } from '~/features/competitions/useCreateCompetitionDialog'

const viewProps = defineProps<{ state: CreateCompetitionDialogViewState }>()
const { open, canOrganize, title, description, mode, startTime, endTime, teamRegistrationAutoApprove, allowTeamRegistrationWhileRunning, maxTeamMembers, maxConcurrentRuntimeInstancesPerTeam, maxActiveQuestionsPerTeam, maxParticipantMessagesBeforeHandlerReply, allowChallengeOwnersToHandleQuestions, practiceModeEnabled, staffOnly, posterError, posterInputKey, submitLabel, error, pending, selectPoster, setOpen, submit } = toRefs(viewProps.state)
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col p-0 sm:max-w-3xl">
      <DialogHeader class="px-6 pt-6">
        <DialogTitle>{{ $t('competitions.label.newCompetition') }}</DialogTitle>
        <DialogDescription>{{ $t('competitions.createCompetition.validation.gameModeFormat') }}</DialogDescription>
      </DialogHeader>

      <Alert v-if="!canOrganize" variant="destructive" class="mx-6">
        <AlertDescription>{{ $t('competitions.createCompetition.description.accountPermissionCreateCompetitions') }}</AlertDescription>
      </Alert>

      <UiForm class="flex min-h-0 flex-1 flex-col gap-5" @submit.prevent="submit">
        <ScrollSurface axis="y" class="min-h-0 flex-1 overscroll-contain" :aria-label="$t('competitions.label.newCompetition')">
          <FieldGroup class="px-6 pb-1">
          <Alert v-if="error" variant="destructive">
            <AlertDescription>{{ $message(error) }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="create-competition-title">{{ $t('common.label.title') }}</FieldLabel>
            <Input id="create-competition-title" v-model="title" required maxlength="200" />
          </Field>
          <Field>
            <FieldLabel for="create-competition-description">{{ $t('common.label.description') }}</FieldLabel>
            <Textarea id="create-competition-description" v-model="description" :placeholder="$t('common.label.optional')" />
          </Field>
          <Field>
            <FieldLabel for="create-competition-mode">{{ $t('common.label.gameMode') }}</FieldLabel>
            <Select v-model="mode">
              <SelectTrigger id="create-competition-mode" class="w-full">
                <SelectValue :placeholder="$t('common.label.selectMode')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem v-for="option in state.modeOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
            <FieldDescription>{{ $t('competitions.createCompetition.validation.modifiedCreationFormat') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-staff-only" v-model="staffOnly" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-staff-only" class="font-normal">{{ $t('competitionAccess.hidden') }}</FieldLabel>
              <FieldDescription>{{ $t('competitionAccess.description') }}</FieldDescription>
            </div>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="create-competition-start">{{ $t('common.label.startTime') }}</FieldLabel>
              <DateTimePicker id="create-competition-start" v-model="startTime" required />
            </Field>
            <Field>
              <FieldLabel for="create-competition-end">{{ $t('common.label.endTime') }}</FieldLabel>
              <DateTimePicker id="create-competition-end" v-model="endTime" required />
            </Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="create-competition-max-members">{{ $t('common.createCompetition.description.maximumNumberPeopleTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-members" v-model.number="maxTeamMembers" min="1" required />
            </Field>
            <Field>
              <FieldLabel for="create-competition-max-runtime">{{ $t('common.createCompetition.label.maximumConcurrentRuntimeTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" min="1" required />
            </Field>
          </div>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-auto-approve" v-model="teamRegistrationAutoApprove" />
            <FieldLabel for="create-competition-auto-approve" class="font-normal">{{ $t('competitions.createCompetition.validation.teamRegistrationRequired') }}</FieldLabel>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-running-registration" v-model="allowTeamRegistrationWhileRunning" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-running-registration" class="font-normal">{{ $t('common.createCompetition.description.teamCreationStillAllowed') }}</FieldLabel>
              <FieldDescription>{{ $t('common.createCompetition.description.closedCompetitionStopAccepting') }}</FieldDescription>
            </div>
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel for="create-competition-max-questions">{{ $t('common.createCompetition.label.activeConsultationLimitTeam') }}</FieldLabel>
              <NumberInput id="create-competition-max-questions" v-model.number="maxActiveQuestionsPerTeam" min="1" required />
              <FieldDescription>{{ $t('common.createCompetition.description.pendingRespondedInquiriesCount') }}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="create-competition-max-messages">{{ $t('common.createCompetition.description.maximumNumberContinuousSupplementary') }}</FieldLabel>
              <NumberInput id="create-competition-max-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" min="1" required />
              <FieldDescription>{{ $t('common.createCompetition.description.initialQuestionsIncludedQuota') }}</FieldDescription>
            </Field>
          </div>
          <Field orientation="horizontal">
            <Checkbox id="create-competition-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-owner-questions" class="font-normal">{{ $t('common.createCompetition.description.allowQuestionOwnersHandle') }}</FieldLabel>
              <FieldDescription>{{ $t('common.createCompetition.description.topicsOwnsCollaboratesFlag') }}</FieldDescription>
            </div>
          </Field>
          <Field v-if="mode === 'Ctf'" orientation="horizontal">
            <Checkbox id="create-competition-practice-mode" v-model="practiceModeEnabled" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="create-competition-practice-mode" class="font-normal">{{ $t('competitions.createCompetition.description.enablePracticeModeCompetition') }}</FieldLabel>
              <FieldDescription>{{ $t('competitions.createCompetition.description.approvedNonBannedCompetition') }}</FieldDescription>
            </div>
          </Field>
          <Field>
            <FieldLabel for="create-competition-poster">{{ $t('common.label.competitionPoster') }}</FieldLabel>
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
          <Button type="button" variant="outline" :disabled="pending" @click="setOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button type="submit" :disabled="pending || !canOrganize || Boolean(posterError)">
            <Spinner v-if="pending" data-icon="inline-start" />{{ submitLabel }}
          </Button>
        </DialogFooter>
      </UiForm>
    </DialogContent>
  </Dialog>
</template>
