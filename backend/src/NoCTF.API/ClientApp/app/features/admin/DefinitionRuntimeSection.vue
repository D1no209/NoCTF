<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'

import { useDefinitionRuntimeSection } from './useDefinitionRuntimeSection'
import View from '~/components/views/admin/DefinitionRuntimeSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionRuntimeSection(props))

</script>

<template>
  <FeatureView />
</template>
