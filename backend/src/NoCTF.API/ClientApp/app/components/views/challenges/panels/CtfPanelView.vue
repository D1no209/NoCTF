<script setup lang="ts">
import { toRefs } from 'vue'
import type { CtfPanelViewState } from '~/features/challenges/panels/useCtfPanel'

const viewProps = defineProps<{ state: CtfPanelViewState }>()
const { practiceOpen, isPatchVerification, actionsAvailable, patchVerification, patchOutcome, emit, handleEvaluation, handlePatchChanged, FlagSubmit, FixSubmit, RuntimeCard, setRuntimeCardRef, competition, challenge, flagDockTarget, runtimeDockTarget } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col divide-y">
    <component :is="RuntimeCard"
      v-if="challenge.hasRuntime === true && actionsAvailable"
      :ref="setRuntimeCardRef"
      class="pb-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="full"
      :dock-target="runtimeDockTarget"
    />
    <component :is="FlagSubmit"
      v-if="actionsAvailable && !isPatchVerification"
      :dock-target="flagDockTarget"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      :practice="practiceOpen"
      :initially-solved="challenge.solvedByMyTeam"
      :maximum-attempts="challenge.maximumFlagAttempts"
      :remaining-attempts="challenge.remainingFlagAttempts"
      @evaluated="handleEvaluation"
      @submitted="emit('submitted')"
      @remaining-changed="emit('remainingChanged', $event)"
    />
    <Teleport v-if="actionsAvailable && isPatchVerification" :to="flagDockTarget ?? 'body'" :disabled="!flagDockTarget">
      <component
        :is="FixSubmit"
        class="pt-5"
        :competition-id="competition.id!"
        :competition-challenge-id="challenge.id!"
        :defense="patchVerification"
        ctf-patch-verification
        @changed="handlePatchChanged"
        @accepted="emit('submitted')"
      />
    </Teleport>
    <Alert v-if="isPatchVerification && patchOutcome" :variant="patchOutcome.variant" class="mt-4">
      <AlertDescription>{{ $message(patchOutcome.message) }}</AlertDescription>
    </Alert>
  </div>
</template>
