<script setup lang="ts">
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'

defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()
</script>

<template>
  <div class="flex flex-col gap-6">
    <RuntimeCard
      v-if="competition.status === 'Running' || competition.status === 'Finished' && competition.practiceModeEnabled === true"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="full"
    />
    <FlagSubmit
      v-if="competition.status === 'Running' || competition.status === 'Finished' && competition.practiceModeEnabled === true"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      :practice="competition.status === 'Finished' && competition.practiceModeEnabled === true"
    />
  </div>
</template>
