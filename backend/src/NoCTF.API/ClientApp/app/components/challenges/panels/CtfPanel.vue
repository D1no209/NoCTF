<script setup lang="ts">
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'

defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const emit = defineEmits<{ submitted: [] }>()

const runtimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

function handleEvaluation(result?: string | null): void {
  if (result === 'Correct') void runtimeCard.value?.refreshUntilStopped()
}
</script>

<template>
  <div class="flex flex-col divide-y">
    <RuntimeCard
      v-if="challenge.hasRuntime === true && (competition.status === 'Running' || competition.status === 'Finished' && competition.practiceModeEnabled === true)"
      ref="runtimeCard"
      class="pb-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="full"
    />
    <FlagSubmit
      v-if="competition.status === 'Running' || competition.status === 'Finished' && competition.practiceModeEnabled === true"
      class="pt-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      :practice="competition.status === 'Finished' && competition.practiceModeEnabled === true"
      :maximum-attempts="challenge.maximumFlagAttempts"
      :remaining-attempts="challenge.remainingFlagAttempts"
      @evaluated="handleEvaluation"
      @submitted="emit('submitted')"
    />
  </div>
</template>
