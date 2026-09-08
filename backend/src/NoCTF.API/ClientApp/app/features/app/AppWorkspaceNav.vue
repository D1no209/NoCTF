<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { WorkspaceNavGroup } from '~/features/app/workspace-nav'
import { useAppWorkspaceNav } from './useAppWorkspaceNav'
import View from '~/components/views/app/AppWorkspaceNavView.vue'

const props = defineProps<{
  groups: WorkspaceNavGroup[]
  title?: string
}>()
const state = bindViewState(useAppWorkspaceNav(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
