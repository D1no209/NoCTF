<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '~/api'

import { useModeBadge } from './useModeBadge'
import View from '~/components/views/competitions/ModeBadgeView.vue'

const props = defineProps<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol }>()
const state = bindViewState(useModeBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
