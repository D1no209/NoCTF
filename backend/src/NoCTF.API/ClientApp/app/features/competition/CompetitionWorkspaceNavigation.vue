<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { WorkspaceNavGroup } from '~/features/app/workspace-nav'
import { useCompetitionWorkspaceNavigation } from './useCompetitionWorkspaceNavigation'
import View from '~/components/views/competition/CompetitionWorkspaceNavigationView.vue'

const props = defineProps<{ groups: WorkspaceNavGroup[] }>()
const state = bindViewState(useCompetitionWorkspaceNavigation(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
