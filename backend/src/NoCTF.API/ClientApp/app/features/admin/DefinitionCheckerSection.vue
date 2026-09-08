<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'

import { useDefinitionCheckerSection } from './useDefinitionCheckerSection'
import View from '~/components/views/admin/DefinitionCheckerSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})
const state = bindViewState(useDefinitionCheckerSection(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
