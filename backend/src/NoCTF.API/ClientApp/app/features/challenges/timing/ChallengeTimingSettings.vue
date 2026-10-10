<script setup lang="ts">
import ChallengeTimingSettingsView from '~/components/views/challenges/ChallengeTimingSettingsView.vue'
import { useChallengeTimingSettings } from './useChallengeTimingSettings'
import { bindViewState } from '~/features/shared/view-state'
const props = defineProps<{ competitionId: string; competitionChallengeId: string; canWrite: boolean }>()
const state = bindViewState(useChallengeTimingSettings(props))
</script>
<template><ChallengeTimingSettingsView :state="state" /></template>
