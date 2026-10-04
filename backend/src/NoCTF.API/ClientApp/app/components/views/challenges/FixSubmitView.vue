<script setup lang="ts">
import { toRefs } from 'vue'
import type { FixSubmitViewState } from '~/features/challenges/useFixSubmit'

const viewProps = defineProps<{ state: FixSubmitViewState }>()
const { file, pendingAction, targetCreating, canUpload, validating, recycling, patchWaitingForTarget, targetFailed, completedAndRecycled, canRequest, onFileChange, requestTarget, uploadFix, competitionChallengeId, ctfPatchVerification } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex flex-col gap-4" aria-labelledby="fix-submit-title">
    <h3 id="fix-submit-title" class="text-sm font-semibold">{{ ctfPatchVerification ? $t('common.label.patchVerification') : $t('common.label.defenseVerification') }}</h3>
    <div class="flex flex-col gap-4">
      <UiForm v-if="canUpload" class="flex flex-col gap-4" @submit.prevent="uploadFix">
        <p class="flex items-start gap-2 text-sm text-muted-foreground" role="status" aria-live="polite">
          <Spinner v-if="targetCreating" class="mr-2 inline size-3" />
          <span>
            {{ targetCreating
              ? $t('challenges.fixSubmit.description.defenseEnvironmentStartingUpload')
              : $t('challenges.fixSubmit.description.verificationEnvironmentReadyUpload') }}
          </span>
        </p>
        <FieldGroup>
          <Field>
            <FieldLabel :for="`patch-file-${competitionChallengeId}`">{{ $t('challenges.fixSubmit.description.fixArchiveAttemptTar') }}</FieldLabel>
            <FileUpload
              :pending="pendingAction === 'upload'"
              :id="`patch-file-${competitionChallengeId}`"

              accept=".tar.gz,.tgz,application/gzip"
              :disabled="pendingAction !== null"
              @change="onFileChange"
            />
            <FieldDescription v-if="file">
              {{ $t('challenges.label.fixSubmit', { file: file.name, size: formatBytes(file.size) }) }}
            </FieldDescription>
          </Field>
          <Field>
            <Button type="submit" :disabled="pendingAction !== null || !file">
              <Spinner v-if="pendingAction === 'upload'" data-icon="inline-start" />
              {{ pendingAction === 'upload' ? $t('challenges.label.uploadingLocking') : $t('challenges.label.uploadFixPackage') }}
            </Button>
          </Field>
        </FieldGroup>
      </UiForm>

      <p v-else-if="validating || recycling || completedAndRecycled" class="flex items-center gap-2 text-sm text-muted-foreground" role="status" aria-live="polite">
        <Spinner v-if="validating || recycling" class="size-3" />
        <span v-if="validating">
          {{ patchWaitingForTarget
            ? $t('challenges.fixSubmit.description.fixUploadedVerificationStart')
            : $t('challenges.label.verifyingFix') }}
        </span>
        <span v-else-if="recycling">{{ $t('challenges.fixSubmit.label.verificationEndedFinalizing') }}</span>
        <span v-else>{{ $t('challenges.fixSubmit.label.fixVerificationComplete') }}</span>
      </p>

      <Alert v-else-if="targetFailed" variant="destructive">
        <AlertDescription>
          {{ $t('challenges.fixSubmit.error.defenseVerificationEnvironmentFailed.fixSubmitView') }}
        </AlertDescription>
      </Alert>

      <Button v-if="canRequest" :disabled="pendingAction !== null" @click="requestTarget">
        <Spinner v-if="pendingAction === 'request'" data-icon="inline-start" />
        {{ pendingAction === 'request'
          ? $t('challenges.label.requesting')
          : ctfPatchVerification
            ? completedAndRecycled || targetFailed ? $t('challenges.label.patchVerificationAgain') : $t('challenges.label.patchVerificationTarget')
            : completedAndRecycled || targetFailed ? $t('challenges.label.defenseAgain') : $t('challenges.label.defenseEnvironment') }}
      </Button>
    </div>
  </section>
</template>
