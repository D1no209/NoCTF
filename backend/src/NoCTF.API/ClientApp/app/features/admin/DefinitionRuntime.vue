<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel, GameModeValue, RuntimeTemplateModel } from '~/utils/game-config'

import { useDefinitionRuntime } from './useDefinitionRuntime'
import View from '~/components/views/admin/DefinitionRuntimeView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  runtime: RuntimeTemplateModel
  mode: GameModeValue
  interactionKind: DefinitionModel['interactionKind']
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionRuntime(props))

</script>

<template>
  <FeatureView />
</template>
