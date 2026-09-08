<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { DefinitionModel } from '~/utils/game-config'

import { useDefinitionPatchSection } from './useDefinitionPatchSection'
import View from '~/components/views/admin/DefinitionPatchSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const state = bindViewState(useDefinitionPatchSection(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
