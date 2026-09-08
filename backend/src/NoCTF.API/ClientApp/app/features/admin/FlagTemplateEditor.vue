<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { FlagTemplateModel } from '~/utils/game-config'
import { useFlagTemplateEditor } from './useFlagTemplateEditor'
import View from '~/components/views/admin/FlagTemplateEditorView.vue'

const props = withDefaults(defineProps<{
  template: FlagTemplateModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const state = bindViewState(useFlagTemplateEditor(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
