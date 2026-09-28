<script setup lang="ts">
import { toRefs } from 'vue'
import type { FlagSubmitViewState } from '~/features/challenges/useFlagSubmit'

defineOptions({ inheritAttrs: false })
const viewProps = defineProps<{ state: FlagSubmitViewState }>()
const { PartyPopper, input, submitting, celebrating, persistentResult, solved, celebrationParticles, inputDisabled, timedOut, pollingErrorMessage, submit, competitionChallengeId, multiple, title, description, practice, dockTarget } = toRefs(viewProps.state)
</script>

<template>
  <Teleport :to="dockTarget || 'body'" :disabled="!dockTarget">
  <section v-bind="$attrs" data-slot="flag-submit" class="relative isolate flex flex-col gap-4 overflow-visible" aria-labelledby="flag-submit-title">
    <Transition name="flag-celebration">
      <div
        v-if="celebrating"
        role="status"
        aria-live="polite"
        class="flag-celebration-layer"
      >
        <span class="sr-only">{{ $t('ui.flagCorrect') }}</span>
        <span aria-hidden="true" class="flag-celebration-burst">
          <span
            v-for="particle in celebrationParticles"
            :key="particle.id"
            class="flag-celebration-particle"
            :class="particle.tone"
            :style="{
              '--particle-angle': particle.angle,
              '--particle-distance': particle.distance,
              '--particle-delay': particle.delay,
            }"
          />
          <span class="flag-celebration-core">
            <PartyPopper class="size-10" />
          </span>
        </span>
      </div>
    </Transition>
    <h3 id="flag-submit-title" class="sr-only">{{ title }}</h3>
    <p v-if="description" class="text-xs text-muted-foreground">{{ description }}</p>
    <p v-else-if="practice" class="text-xs text-muted-foreground">{{ $t('ui.practiceModeOnlyChecksWhetherAFlagIsCorrectIt') }}</p>
    <div>
      <Alert v-if="persistentResult?.correct === true" class="mb-4" role="status">
        <PartyPopper class="size-5 shrink-0 text-primary" aria-hidden="true" />
        <AlertTitle>{{ $t('ui.flagCorrect') }}</AlertTitle>
        <AlertDescription>{{ $message(persistentResult.message) }}</AlertDescription>
      </Alert>
      <TerminalCommand :id="`flag-input-${competitionChallengeId}`" v-model="input" :label="multiple ? $t('ui.flagListOnePerLine') : $t('ui.flag4')" :multiple="multiple" :pending="submitting" :disabled="inputDisabled" :placeholder="solved ? $t('terminal.challengeSolved') : $t('terminal.flagPlaceholder')" :hint="multiple ? $t('terminal.multipleHint') : $t('terminal.submitHint')" @submit="submit" />

      <Alert v-if="persistentResult && persistentResult.correct !== true" class="mt-4" variant="destructive" role="status"><AlertTitle>{{ persistentResult.correct === null ? $t('ui.noJudgementReceived') : $t('ui.latestJudgement') }}</AlertTitle><AlertDescription>{{ $message(persistentResult.message) }}</AlertDescription></Alert>
      <Alert v-if="timedOut" class="mt-4">
        <AlertDescription>{{ $t('ui.theEvaluationIsTakingLongerThanExpectedContinueTrackingIt') }}</AlertDescription>
      </Alert>
      <Alert v-else-if="pollingErrorMessage" variant="destructive" class="mt-4">
        <AlertDescription>{{ $t('ui.submissionStatusUpdateFailedAndWillRetryAutomatically', { reason: pollingErrorMessage }) }}</AlertDescription>
      </Alert>
    </div>
  </section>
  </Teleport>
</template>
