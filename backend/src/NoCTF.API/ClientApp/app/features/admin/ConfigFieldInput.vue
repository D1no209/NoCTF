<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { ConfigFieldDef } from '~/utils/game-config'

import { useConfigFieldInput } from './useConfigFieldInput'
import View from '~/components/views/admin/ConfigFieldInputView.vue'

const props = withDefaults(defineProps<{
  field: ConfigFieldDef
  modelValue: unknown
  disabled?: boolean
}>(), {
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [value: unknown] }>()
const state = bindViewState(useConfigFieldInput(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
