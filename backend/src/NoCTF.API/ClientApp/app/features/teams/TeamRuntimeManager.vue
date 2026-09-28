<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useTeamRuntimeManager } from './useTeamRuntimeManager'
import View from '~/components/views/teams/TeamRuntimeManagerView.vue'

const props = defineProps<{ competitionId: string }>()
const state = bindViewState(useTeamRuntimeManager(props))
</script>

<template>
  <View :state="state" />
</template>
