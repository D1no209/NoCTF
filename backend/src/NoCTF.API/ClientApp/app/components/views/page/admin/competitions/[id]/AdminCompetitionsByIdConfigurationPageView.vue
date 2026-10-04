<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdConfigurationPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdConfigurationPageViewState }>()
const { competition, canWrite, title, description, startTime, endTime, teamRegistrationAutoApprove, allowTeamRegistrationWhileRunning, maxTeamMembers, maxConcurrentRuntimeInstancesPerTeam, runtimeAccessMode, trafficCaptureEnabled, trafficCaptureLimitMiB, trafficCaptureHasDirectBypass, maxActiveQuestionsPerTeam, maxParticipantMessagesBeforeHandlerReply, allowChallengeOwnersToHandleQuestions, practiceModeEnabled, writeUpSubmissionRequired, writeUpSubmissionDeadlineHours, maximumWriteUpDeadlineHours, staffOnly, savingMeta, metaError, saveMeta, config, configLoading, savingConfig, saveConfig, CompetitionModeConfigEditor } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="gap-0">
      <section id="competition-basic-configuration" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.basicInformation.configurationPageView') }}</CardTitle>
        <CardDescription>{{ $t('administration.competitionsBy.validation.titleTimeFormat') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <UiForm @submit.prevent="saveMeta">
          <FieldGroup>
            <Alert v-if="metaError" variant="destructive">
              <AlertDescription>{{ $message(metaError) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="c-title">{{ $t('common.label.title') }}</FieldLabel>
              <Input id="c-title" v-model="title" :readonly="!canWrite" required />
            </Field>
            <div class="grid min-w-0 gap-4 xl:grid-cols-2">
              <Field>
                <FieldLabel for="c-desc">{{ $t('common.label.description') }}</FieldLabel>
                <Textarea id="c-desc" v-model="description" rows="8" :readonly="!canWrite" />
              </Field>
              <MarkdownPreview :source="description" :label="$t('administration.label.markdownPreview')" :empty-label="$t('administration.label.content')" />
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-start">{{ $t('common.label.startTime') }}</FieldLabel>
                <DateTimePicker id="c-start" v-model="startTime"  :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-end">{{ $t('common.label.endTime') }}</FieldLabel>
                <DateTimePicker id="c-end" v-model="endTime"  :readonly="!canWrite" required />
              </Field>
            </div>
            <Separator />
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-runtime-access-mode">{{ $t('runtime.accessMode') }}</FieldLabel>
                <Select v-model="runtimeAccessMode" :disabled="!canWrite">
                  <SelectTrigger id="c-runtime-access-mode"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Direct">{{ $t('runtime.accessModeDirect') }}</SelectItem>
                    <SelectItem value="DirectAndWsrx">{{ $t('runtime.accessModeDirectAndWsrx') }}</SelectItem>
                    <SelectItem value="WsrxOnly">{{ $t('runtime.accessModeWsrxOnly') }}</SelectItem>
                  </SelectContent>
                </Select>
                <FieldDescription>{{ $t('runtime.accessModeDescription') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="c-traffic-capture-limit">{{ $t('runtime.captureLimitMiB') }}</FieldLabel>
                <NullableNumberInput
                  id="c-traffic-capture-limit"
                  v-model="trafficCaptureLimitMiB"
                  :min="1"
                  :max="4096"
                  :readonly="!canWrite || !trafficCaptureEnabled"
                  :placeholder="$t('runtime.captureLimitDefault')"
                />
                <FieldDescription>{{ $t('runtime.captureLimitDescription') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox
                id="c-traffic-capture"
                v-model="trafficCaptureEnabled"
                :disabled="!canWrite || runtimeAccessMode === 'Direct'"
              />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-traffic-capture" class="font-normal">{{ $t('runtime.captureTraffic') }}</FieldLabel>
                <FieldDescription>{{ $t('runtime.captureTrafficDescription') }}</FieldDescription>
              </div>
            </Field>
            <Alert v-if="trafficCaptureHasDirectBypass">
              <AlertDescription>{{ $t('runtime.captureDirectBypassWarning') }}</AlertDescription>
            </Alert>
            <Field orientation="horizontal">
              <Checkbox id="c-writeup-required" v-model="writeUpSubmissionRequired" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-writeup-required" class="font-normal">{{ $t('writeUp.requireSubmission') }}</FieldLabel>
                <FieldDescription>{{ $t('writeUp.requireSubmissionDescription') }}</FieldDescription>
              </div>
            </Field>
            <Field>
              <FieldLabel for="c-writeup-deadline-hours">{{ $t('writeUp.deadlineHours') }}</FieldLabel>
              <NumberInput
                id="c-writeup-deadline-hours"
                v-model.number="writeUpSubmissionDeadlineHours"
                min="0"
                :max="maximumWriteUpDeadlineHours"
                :readonly="!canWrite"
                required
              />
              <FieldDescription>{{ $t('writeUp.deadlineHoursDescription') }}</FieldDescription>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-members">{{ $t('common.createCompetition.description.maximumNumberPeopleTeam') }}</FieldLabel>
                <NumberInput id="c-max-members" v-model.number="maxTeamMembers"  min="1" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-max-runtime">{{ $t('common.createCompetition.label.maximumConcurrentRuntimeTeam') }}</FieldLabel>
                <NumberInput id="c-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam"  min="1" :readonly="!canWrite" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-auto-approve" v-model="teamRegistrationAutoApprove" :disabled="!canWrite" />
              <FieldLabel for="c-auto-approve" class="font-normal">{{ $t('administration.label.teamRegistrationAutomaticallyPasses') }}</FieldLabel>
            </Field>
            <Field v-if="competition?.mode === 'Ctf'" orientation="horizontal">
              <Checkbox id="c-practice-mode" v-model="practiceModeEnabled" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-practice-mode" class="font-normal">{{ $t('administration.label.postCompetitionPracticeMode') }}</FieldLabel>
                <FieldDescription>{{ $t('administration.competitionsBy.description.competitionFinishesApprovedNon') }}</FieldDescription>
              </div>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="c-staff-only" v-model="staffOnly" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-staff-only" class="font-normal">{{ $t('competitionAccess.hidden') }}</FieldLabel>
                <FieldDescription>{{ $t('competitionAccess.description') }}</FieldDescription>
              </div>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-running-registration" v-model="allowTeamRegistrationWhileRunning" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-running-registration" class="font-normal">{{ $t('common.createCompetition.description.teamCreationStillAllowed') }}</FieldLabel>
                <FieldDescription>{{ $t('common.createCompetition.description.closedCompetitionStopAccepting') }}</FieldDescription>
              </div>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-active-questions">{{ $t('common.createCompetition.label.activeConsultationLimitTeam') }}</FieldLabel>
                <NumberInput id="c-max-active-questions" v-model.number="maxActiveQuestionsPerTeam"  min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('common.createCompetition.description.pendingRespondedInquiriesCount') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="c-max-participant-messages">{{ $t('common.createCompetition.description.maximumNumberContinuousSupplementary') }}</FieldLabel>
                <NumberInput id="c-max-participant-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply"  min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('common.createCompetition.description.initialQuestionsIncludedQuota') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-challenge-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-challenge-owner-questions" class="font-normal">{{ $t('common.createCompetition.description.allowQuestionOwnersHandle') }}</FieldLabel>
                <FieldDescription>{{ $t('common.createCompetition.description.topicsOwnsCollaboratesFlag') }}</FieldDescription>
              </div>
            </Field>
            <Field v-if="canWrite">
              <Button type="submit" :disabled="savingMeta">
                <Spinner v-if="savingMeta" data-icon="inline-start" /> {{ $t('administration.label.saveBasicInformation') }} </Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
      </section>

      <Separator />
      <section id="competition-mode-configuration" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.modeConfiguration') }}</CardTitle>
        <CardDescription>
          {{ $t('administration.label.modeSpecificSettings', { mode: enumLabel(GameModeLabel, config?.mode) }) }}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <component :is="CompetitionModeConfigEditor"
          :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
          :configuration="config?.configuration"
          :readonly="!canWrite"
          :loading="configLoading"
          :saving="savingConfig"
          @save="saveConfig"
        />
      </CardContent>
      </section>
    </Card>
  </div>
</template>
