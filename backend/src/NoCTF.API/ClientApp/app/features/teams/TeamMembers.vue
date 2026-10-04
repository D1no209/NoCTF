<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsTeamsTeamResponse } from '~/api/models'
import { useTeamMembers } from './useTeamMembers'
import View from '~/components/views/teams/TeamMembersView.vue'

const props = defineProps<{
  competitionId: string
  team: NoCTFAPIEndpointsTeamsTeamResponse
  /** 队长视角:可移除成员 */
  canManage?: boolean
}>()
const emit = defineEmits<{ changed: [] }>()
const state = bindViewState(useTeamMembers(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
