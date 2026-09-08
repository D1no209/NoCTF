<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { ContainerDefinitionModel, GameModeValue } from '~/utils/game-config'
import { FlagSource } from '~/utils/game-config'
import { useDefinitionContainer } from './useDefinitionContainer'
import View from '~/components/views/admin/DefinitionContainerView.vue'

const props = withDefaults(defineProps<{
  definition: ContainerDefinitionModel
  mode: GameModeValue
  /** 运行时的 Flag 来源;静态 Flag 时不能配置注入环境变量。 */
  flagSource?: number
  disabled?: boolean
}>(), {
  flagSource: FlagSource.Static,
  disabled: false,
})
const state = bindViewState(useDefinitionContainer(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
