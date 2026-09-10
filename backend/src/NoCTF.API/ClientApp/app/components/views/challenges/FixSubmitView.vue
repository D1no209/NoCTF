<script setup lang="ts">
import { toRefs } from 'vue'
import type { FixSubmitViewState } from '~/features/challenges/useFixSubmit'

const viewProps = defineProps<{ state: FixSubmitViewState }>()
const { file, pendingAction, targetCreating, canUpload, validating, recycling, patchWaitingForTarget, targetFailed, completedAndRecycled, canRequest, onFileChange, requestTarget, uploadFix, competitionChallengeId } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex flex-col gap-4" aria-labelledby="fix-submit-title">
    <h3 id="fix-submit-title" class="text-sm font-semibold">{{ $t('ui.defenseVerification') }}</h3>
    <div class="flex flex-col gap-4">
      <UiForm v-if="canUpload" class="flex flex-col gap-4" @submit.prevent="uploadFix">
        <Alert>
          <Spinner v-if="targetCreating" class="mr-2 inline size-3" />
          <AlertDescription>
            {{ targetCreating
              ? $t('ui.theDefenseEnvironmentIsStartingYouCanUploadTheFix')
              : $t('ui.theVerificationEnvironmentIsReadyUploadThisFixPackage') }}
          </AlertDescription>
        </Alert>
        <FieldGroup>
          <Field>
            <FieldLabel :for="`patch-file-${competitionChallengeId}`">{{ $t('ui.fixArchiveForThisAttemptTarGz') }}</FieldLabel>
            <FileUpload
              :pending="pendingAction === 'upload'"
              :id="`patch-file-${competitionChallengeId}`"

              accept=".tar.gz,.tgz,application/gzip"
              :disabled="pendingAction !== null"
              @change="onFileChange"
            />
            <FieldDescription v-if="file">
              {{ $t('ui.selected', { file: file.name, size: formatBytes(file.size) }) }}
            </FieldDescription>
          </Field>
          <Field>
            <Button type="submit" :disabled="pendingAction !== null || !file">
              <Spinner v-if="pendingAction === 'upload'" data-icon="inline-start" />
              {{ pendingAction === 'upload' ? $t('ui.uploadingAndLocking') : $t('ui.uploadThisFixPackage') }}
            </Button>
          </Field>
        </FieldGroup>
      </UiForm>

      <Alert v-else-if="validating">
        <Spinner class="mr-2 inline size-3" />
        <AlertDescription class="inline">
          {{ patchWaitingForTarget
            ? $t('ui.theFixHasBeenUploadedVerificationWillStartAutomaticallyWhen')
            : $t('ui.verifyingThisFix') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="recycling">
        <Spinner class="mr-2 inline size-3" />
        <AlertDescription class="inline">
          {{ $t('ui.thisVerificationHasEndedFinalizing') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="completedAndRecycled">
        <AlertDescription>
          {{ $t('ui.thisFixVerificationIsComplete') }}
        </AlertDescription>
      </Alert>

      <Alert v-else-if="targetFailed" variant="destructive">
        <AlertDescription>
          {{ $t('ui.theDefenseVerificationEnvironmentFailedToStartOrExpiredRequest') }}
        </AlertDescription>
      </Alert>

      <Button v-if="canRequest" :disabled="pendingAction !== null" @click="requestTarget">
        <Spinner v-if="pendingAction === 'request'" data-icon="inline-start" />
        {{ pendingAction === 'request' ? $t('ui.requesting') : completedAndRecycled || targetFailed ? $t('ui.requestDefenseAgain') : $t('ui.requestDefenseEnvironment') }}
      </Button>
    </div>
  </section>
</template>
