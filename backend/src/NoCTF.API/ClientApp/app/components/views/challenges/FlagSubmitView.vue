<script setup lang="ts">
import { toRefs } from 'vue'
import type { FlagSubmitViewState } from '~/features/challenges/useFlagSubmit'

const viewProps = defineProps<{ state: FlagSubmitViewState }>()
const { PartyPopper, input, submitting, celebrating, persistentResult, resultDialog, remainingAttempts, celebrationParticles, attemptsExhausted, closeResultDialog, timedOut, pollingErrorMessage, submit, competitionChallengeId, multiple, title, description, practice, readOnlyJudgement, onUpdateOpenOpen } = toRefs(viewProps.state)
</script>

<template>
  <section class="relative flex flex-col gap-4 overflow-hidden" aria-labelledby="flag-submit-title">
    <Transition name="flag-celebration">
      <div
        v-if="celebrating"
        role="status"
        aria-live="polite"
        class="pointer-events-none absolute inset-0 z-10 grid place-items-center overflow-hidden"
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
    <header class="flex flex-col gap-1">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h3 id="flag-submit-title" class="text-sm font-semibold">{{ title }}</h3>
        <Badge v-if="remainingAttempts !== null && !readOnlyJudgement && !practice" :variant="attemptsExhausted ? 'destructive' : 'secondary'">
          {{ attemptsExhausted ? $t('ui.noSubmissionAttemptsRemain') : $t('ui.submissionsRemaining', { count: remainingAttempts }) }}
        </Badge>
      </div>
      <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
      <p v-else-if="practice" class="text-sm text-muted-foreground">{{ $t('ui.practiceModeOnlyChecksWhetherAFlagIsCorrectIt') }}</p>
    </header>
    <div>
      <form @submit.prevent="submit">
        <FieldGroup>
          <Field>
            <FieldLabel :for="`flag-input-${competitionChallengeId}`">
              {{ multiple ? $t('ui.flagListOnePerLine') : $t('ui.flag4') }}
            </FieldLabel>
            <Textarea
              v-if="multiple"
              :id="`flag-input-${competitionChallengeId}`"
              v-model="input"
              rows="4"
              class="font-mono"
              :placeholder="$t('ui.flag3')"
            />
            <Input
              v-else
              :id="`flag-input-${competitionChallengeId}`"
              v-model="input"
              class="font-mono"
              :placeholder="$t('ui.flag3')"
            />
          </Field>
          <Field>
            <Button type="submit" :disabled="submitting || !input.trim() || attemptsExhausted">
              <Spinner v-if="submitting" data-icon="inline-start" /> {{ $t('ui.submissions') }} </Button>
          </Field>
        </FieldGroup>
      </form>

      <Alert v-if="persistentResult" class="mt-4" :variant="persistentResult.correct === true ? 'default' : 'destructive'" role="status"><AlertTitle>{{ persistentResult.correct === null ? $t('ui.noJudgementReceived') : $t('ui.latestJudgement') }}</AlertTitle><AlertDescription>{{ $message(persistentResult.message) }}</AlertDescription></Alert>
      <Alert v-if="timedOut" class="mt-4">
        <AlertDescription>{{ $t('ui.theEvaluationIsTakingLongerThanExpectedContinueTrackingIt') }}</AlertDescription>
      </Alert>
      <Alert v-else-if="pollingErrorMessage" variant="destructive" class="mt-4">
        <AlertDescription>{{ $t('ui.submissionStatusUpdateFailedAndWillRetryAutomatically', { reason: pollingErrorMessage }) }}</AlertDescription>
      </Alert>
    </div>
  </section>

  <Dialog :open="resultDialog !== null" @update:open="onUpdateOpenOpen">
    <DialogContent class="sm:max-w-md" @pointer-down-outside="closeResultDialog">
      <DialogHeader>
        <DialogTitle>{{ resultDialog?.title }}</DialogTitle>
        <DialogDescription>{{ $message(resultDialog?.message) }}</DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button @click="closeResultDialog">{{ $t('ui.ok') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.flag-celebration-burst {
  position: relative;
  display: grid;
  width: 10rem;
  height: 10rem;
  place-items: center;
}

.flag-celebration-core {
  display: grid;
  width: 4rem;
  height: 4rem;
  place-items: center;
  border: 1px solid var(--border);
  border-radius: 9999px;
  color: var(--primary);
  background: var(--background);
  animation: flag-celebration-core 720ms cubic-bezier(0.16, 1, 0.3, 1) both;
}

.flag-celebration-particle {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 0.3rem;
  height: 0.8rem;
  border-radius: 9999px;
  background: currentColor;
  opacity: 0;
  transform-origin: 50% 0;
  animation: flag-celebration-particle 760ms cubic-bezier(0.16, 1, 0.3, 1) var(--particle-delay) both;
}

.flag-celebration-leave-active {
  transition: opacity 160ms cubic-bezier(0.25, 1, 0.5, 1);
}

.flag-celebration-leave-to {
  opacity: 0;
}

@keyframes flag-celebration-core {
  0% { opacity: 0; transform: scale(0.55) rotate(-12deg); }
  45% { opacity: 1; transform: scale(1.08) rotate(4deg); }
  100% { opacity: 1; transform: scale(1) rotate(0); }
}

@keyframes flag-celebration-particle {
  0% {
    opacity: 0;
    transform: rotate(var(--particle-angle)) translateY(-0.6rem) scaleY(0.45);
  }
  18% { opacity: 1; }
  100% {
    opacity: 0;
    transform: rotate(var(--particle-angle)) translateY(var(--particle-distance)) scaleY(1);
  }
}

@media (prefers-reduced-motion: reduce) {
  .flag-celebration-core,
  .flag-celebration-particle {
    animation: none;
  }

  .flag-celebration-leave-active {
    transition: none;
  }
}
</style>
