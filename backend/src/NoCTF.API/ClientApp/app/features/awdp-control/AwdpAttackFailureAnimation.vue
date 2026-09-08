<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { AwdpControlEvent } from '~/utils/awdp-control-screen'
import { useAwdpAttackFailureAnimation } from './useAwdpAttackFailureAnimation'
import View from '~/components/views/awdp-control/AwdpAttackFailureAnimationView.vue'

const props = defineProps<{ event: AwdpControlEvent }>()
const state = bindViewState(useAwdpAttackFailureAnimation(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
