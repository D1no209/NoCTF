<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'

import { useCompetitionStatusBadge } from './useCompetitionStatusBadge'
import View from '~/components/views/admin/CompetitionStatusBadgeView.vue'

const props = defineProps<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | null }>()
const state = bindViewState(useCompetitionStatusBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
