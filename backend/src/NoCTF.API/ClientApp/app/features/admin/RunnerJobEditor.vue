<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { RunnerJobModel } from '~/utils/game-config'
import { useRunnerJobEditor } from './useRunnerJobEditor'
import View from '~/components/views/admin/RunnerJobEditorView.vue'

const props = withDefaults(defineProps<{
  job: RunnerJobModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const state = bindViewState(useRunnerJobEditor(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
