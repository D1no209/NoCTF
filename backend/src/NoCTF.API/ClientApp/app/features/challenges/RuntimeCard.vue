<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useRuntimeCard } from './useRuntimeCard'
import View from '~/components/views/challenges/RuntimeCardView.vue'

const props = withDefaults(
  defineProps<{
    competitionId: string
    competitionChallengeId: string
    /** full = CTF 全操作;reset-only = AWD 仅重置;readonly = 只显示最终状态 */
    controls?: 'full' | 'reset-only' | 'readonly'
    dockTarget?: string
  }>(),
  { controls: 'full', dockTarget: '' },
)
const state = bindViewState(useRuntimeCard(props))
defineExpose({ refreshUntilStopped: state.refreshUntilStopped })
</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
