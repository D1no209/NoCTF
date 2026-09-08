<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdConfigurationPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdConfigurationPageViewState }>()
const { competition, canWrite, title, description, startTime, endTime, teamRegistrationAutoApprove, allowTeamRegistrationWhileRunning, maxTeamMembers, maxConcurrentRuntimeInstancesPerTeam, maxActiveQuestionsPerTeam, maxParticipantMessagesBeforeHandlerReply, allowChallengeOwnersToHandleQuestions, practiceModeEnabled, savingMeta, metaError, saveMeta, config, configLoading, savingConfig, saveConfig, CompetitionModeConfigEditor } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.basicInformation2') }}</CardTitle>
        <CardDescription>{{ $t('ui.titleTimeAndTeamRestrictionsGameModeCannotBeModified') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="saveMeta">
          <FieldGroup>
            <Alert v-if="metaError" variant="destructive">
              <AlertDescription>{{ $message(metaError) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="c-title">{{ $t('ui.title') }}</FieldLabel>
              <Input id="c-title" v-model="title" :readonly="!canWrite" required />
            </Field>
            <Field>
              <FieldLabel for="c-desc">{{ $t('ui.description') }}</FieldLabel>
              <Textarea id="c-desc" v-model="description" :readonly="!canWrite" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-start">{{ $t('ui.startTime') }}</FieldLabel>
                <Input id="c-start" v-model="startTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-end">{{ $t('ui.endTime') }}</FieldLabel>
                <Input id="c-end" v-model="endTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-members">{{ $t('ui.maximumNumberOfPeoplePerTeam') }}</FieldLabel>
                <Input id="c-max-members" v-model.number="maxTeamMembers" type="number" min="1" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-max-runtime">{{ $t('ui.maximumConcurrentRuntimePerTeam') }}</FieldLabel>
                <Input id="c-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" :readonly="!canWrite" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-auto-approve" v-model="teamRegistrationAutoApprove" :disabled="!canWrite" />
              <FieldLabel for="c-auto-approve" class="font-normal">{{ $t('ui.teamRegistrationAutomaticallyPasses') }}</FieldLabel>
            </Field>
            <Field v-if="competition?.mode === 'Ctf'" orientation="horizontal">
              <Checkbox id="c-practice-mode" v-model="practiceModeEnabled" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-practice-mode" class="font-normal">{{ $t('ui.postCompetitionPracticeMode') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.afterTheCompetitionFinishesApprovedAndNonBannedTeamsCan') }}</FieldDescription>
              </div>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-running-registration" v-model="allowTeamRegistrationWhileRunning" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-running-registration" class="font-normal">{{ $t('ui.teamCreationIsStillAllowedDuringTheGame') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.whenClosedTheCompetitionWillStopAcceptingNewTeamsAnd') }}</FieldDescription>
              </div>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-active-questions">{{ $t('ui.activeConsultationLimitPerTeam') }}</FieldLabel>
                <Input id="c-max-active-questions" v-model.number="maxActiveQuestionsPerTeam" type="number" min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('ui.pendingAndRespondedInquiriesCountTowardsTheCap') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="c-max-participant-messages">{{ $t('ui.maximumNumberOfContinuousSupplementaryMessages') }}</FieldLabel>
                <Input id="c-max-participant-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" type="number" min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('ui.initialQuestionsAreIncludedInTheQuotaTheyWillBe') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-challenge-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-challenge-owner-questions" class="font-normal">{{ $t('ui.allowQuestionOwnersToHandleRelatedInquiries') }}</FieldLabel>
                <FieldDescription>{{ $t('ui.onlyForTopicsItOwnsOrCollaboratesOnNoFlag') }}</FieldDescription>
              </div>
            </Field>
            <Field v-if="canWrite">
              <Button type="submit" :disabled="savingMeta">
                <Spinner v-if="savingMeta" data-icon="inline-start" /> {{ $t('ui.saveBasicInformation') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.modeConfiguration') }}</CardTitle>
        <CardDescription>
          {{ $t('ui.modeSpecificSettingsFor', { mode: enumLabel(GameModeLabel, config?.mode) }) }}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <component :is="CompetitionModeConfigEditor"
          :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
          :json="config?.json"
          :readonly="!canWrite"
          :loading="configLoading"
          :saving="savingConfig"
          @save="saveConfig"
        />
      </CardContent>
    </Card>
  </div>
</template>
