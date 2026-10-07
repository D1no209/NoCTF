<script setup lang="ts">
import { bindViewState } from '../shared/view-state'
import { useCompetitionTeamBanScreen } from './useCompetitionTeamBanScreen'
import View from '../../components/views/competition/CompetitionTeamBanScreenView.vue'

const props = defineProps<{ competitionId: string; teamId: string; teamName?: string; teamAvatarUrl?: string | null }>()
const emit = defineEmits<{ 'refresh-team': [] }>()
const state = bindViewState(useCompetitionTeamBanScreen(props, () => emit('refresh-team')))
</script>

<template>
  <View :state="state" />
</template>
