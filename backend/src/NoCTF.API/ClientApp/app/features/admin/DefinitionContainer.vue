<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { ContainerDefinitionModel, DefinitionModel, GameModeValue, RuntimeTemplateModel } from '~/utils/game-config'
import { useDefinitionContainer } from './useDefinitionContainer'
import View from '~/components/views/admin/DefinitionContainerView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  singleServiceOnly: boolean
  definition: ContainerDefinitionModel
  mode: GameModeValue
  /** 运行时的 Flag 来源;静态 Flag 时不能配置注入环境变量。 */
  flagSource?: RuntimeTemplateModel['flagSource']
  disabled?: boolean
}>(), {
  flagSource: 'Static',
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionContainer(props))

</script>

<template>
  <FeatureView />
</template>
