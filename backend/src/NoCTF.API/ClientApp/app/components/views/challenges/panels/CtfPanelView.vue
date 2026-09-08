<script setup lang="ts">
import { toRefs } from 'vue'
import type { CtfPanelViewState } from '~/features/challenges/panels/useCtfPanel'

const viewProps = defineProps<{ state: CtfPanelViewState }>()
const { practiceOpen, actionsAvailable, emit, handleEvaluation, FlagSubmit, RuntimeCard, setRuntimeCardRef, competition, challenge } = toRefs(viewProps.state)
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
    />
    <component :is="FlagSubmit"
      v-if="actionsAvailable"
      class="pt-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      :practice="practiceOpen"
      :maximum-attempts="challenge.maximumFlagAttempts"
      :remaining-attempts="challenge.remainingFlagAttempts"
      @evaluated="handleEvaluation"
      @submitted="emit('submitted')"
    />
  </div>
</template>
