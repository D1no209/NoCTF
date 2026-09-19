<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { ComposeDefinitionModel } from '~/utils/game-config'
import { FlagSource } from '~/utils/game-config'

import { useDefinitionCompose } from './useDefinitionCompose'
import View from '~/components/views/admin/DefinitionComposeView.vue'

const props = withDefaults(defineProps<{
  definition: ComposeDefinitionModel
  flagSource?: number
  disabled?: boolean
}>(), {
  flagSource: FlagSource.Static,
  disabled: false,
})
const state = bindViewState(useDefinitionCompose(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
