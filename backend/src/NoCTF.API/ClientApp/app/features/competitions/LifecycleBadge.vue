<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'

import { useLifecycleBadge } from './useLifecycleBadge'
import View from '~/components/views/competitions/LifecycleBadgeView.vue'

const props = defineProps<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol }>()
const state = bindViewState(useLifecycleBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
