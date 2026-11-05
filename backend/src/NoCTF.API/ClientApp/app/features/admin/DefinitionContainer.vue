<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { ContainerDefinitionModel, DefinitionModel } from '~/utils/game-config'
import { useDefinitionContainer } from './useDefinitionContainer'
import View from '~/components/views/admin/DefinitionContainerView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  singleServiceOnly: boolean
  definition: ContainerDefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionContainer(props))

</script>

<template>
  <FeatureView />
</template>
