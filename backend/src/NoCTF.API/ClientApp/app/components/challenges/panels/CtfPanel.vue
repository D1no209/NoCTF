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
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="full"
    />
    <FlagSubmit
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
    />
  </div>
</template>
