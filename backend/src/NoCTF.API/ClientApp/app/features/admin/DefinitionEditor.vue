<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { GameModeValue } from '~/utils/game-config'
import { useDefinitionEditor } from './useDefinitionEditor'
import View from '~/components/views/admin/DefinitionEditorView.vue'

const props = withDefaults(defineProps<{
  /** definitionJson 字符串(v-model)。 */
  modelValue: string
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [json: string] }>()
const state = bindViewState(useDefinitionEditor(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
