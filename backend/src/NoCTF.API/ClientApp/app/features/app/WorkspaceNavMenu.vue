<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { WorkspaceNavGroup } from '~/features/app/workspace-nav'

import { useWorkspaceNavMenu } from './useWorkspaceNavMenu'
import View from '~/components/views/app/WorkspaceNavMenuView.vue'

const props = defineProps<{
  groups: WorkspaceNavGroup[]
}>()
const state = bindViewState(useWorkspaceNavMenu(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
