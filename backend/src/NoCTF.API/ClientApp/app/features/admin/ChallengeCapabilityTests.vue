<script setup lang="ts">
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../../api'
import { bindViewState } from '../shared/view-state'
import { useChallengeCapabilityTests } from './useChallengeCapabilityTests'
import View from '../../components/views/admin/ChallengeCapabilityTestsView.vue'

const props = defineProps<{
  challengeId: string
  definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract
  definitionDirty: boolean
}>()
const state = bindViewState(useChallengeCapabilityTests(props))
</script>

<template><View :state="state" :definition-dirty="definitionDirty" /></template>
