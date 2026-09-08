<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { AwdpControlEvent } from '~/utils/awdp-control-screen'
import { useAwdpDefenseSuccessAnimation } from './useAwdpDefenseSuccessAnimation'
import View from '~/components/views/awdp-control/AwdpDefenseSuccessAnimationView.vue'

const props = defineProps<{ event: AwdpControlEvent }>()
const state = bindViewState(useAwdpDefenseSuccessAnimation(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
