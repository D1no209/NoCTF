<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { DefinitionModel, GameModeValue, RuntimeTemplateModel } from '~/utils/game-config'

import { useDefinitionRuntime } from './useDefinitionRuntime'
import View from '~/components/views/admin/DefinitionRuntimeView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  runtime: RuntimeTemplateModel
  mode: GameModeValue
  interactionKind: number
  disabled?: boolean
}>(), {
  disabled: false,
})
const state = bindViewState(useDefinitionRuntime(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
