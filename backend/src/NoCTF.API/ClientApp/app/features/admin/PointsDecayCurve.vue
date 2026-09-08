<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { PointsCurveValue } from '~/utils/game-config'

import { usePointsDecayCurve } from './usePointsDecayCurve'
import View from '~/components/views/admin/PointsDecayCurveView.vue'

const props = defineProps<{ curve: PointsCurveValue }>()
const state = bindViewState(usePointsDecayCurve(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
