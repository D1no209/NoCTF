<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsChallengesChallengeResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse } from '~/api/models'
import { useKohPanel } from './useKohPanel'
import View from '~/components/views/challenges/panels/KohPanelView.vue'

const props = defineProps<{
  competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse
  challenge: NoCTFAPIEndpointsChallengesChallengeResponse
}>()
const state = bindViewState(useKohPanel(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
