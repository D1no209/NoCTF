<script setup lang="ts">
import { bindViewState } from '../shared/view-state'
import { useCompetitionTeamBanScreen } from './useCompetitionTeamBanScreen'
import View from '../../components/views/competition/CompetitionTeamBanScreenView.vue'

const props = defineProps<{ competitionId: string; teamId: string }>()
const state = bindViewState(useCompetitionTeamBanScreen(props))
</script>

<template>
  <View :state="state" />
</template>
