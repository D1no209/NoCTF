<script setup lang="ts">
import { isCtfPracticeOpen } from '~/lib/competition-participation'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const practiceOpen = computed(() => isCtfPracticeOpen(props.competition))
const actionsAvailable = computed(() => props.competition.status === 'Running' || practiceOpen.value)

const emit = defineEmits<{ submitted: [] }>()

const runtimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

function handleEvaluation(result?: string | null): void {
  if (result === 'Correct') void runtimeCard.value?.refreshUntilStopped()
}
</script>

<template>
  <div class="flex flex-col divide-y">
    <RuntimeCard
      v-if="challenge.hasRuntime === true && actionsAvailable"
      ref="runtimeCard"
      class="pb-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="full"
    />
    <FlagSubmit
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
