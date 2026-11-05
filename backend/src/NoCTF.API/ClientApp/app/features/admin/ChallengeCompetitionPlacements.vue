<script setup lang="ts">
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeCapabilityProtocol } from '~/api'
import { bindViewState } from '~/features/shared/view-state'
import { useChallengeCompetitionPlacements } from './useChallengeCompetitionPlacements'
import View from '~/components/views/admin/ChallengeCompetitionPlacementsView.vue'

const props = defineProps<{ challengeId: string; capabilities: NoCtfapiEndpointsAdministrationChallengeBankChallengeCapabilityProtocol[]; disabled: boolean }>()
const state = bindViewState(useChallengeCompetitionPlacements(props))
</script>

<template><View :state="state" /></template>
