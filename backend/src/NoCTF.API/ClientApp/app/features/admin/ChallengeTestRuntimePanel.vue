<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse } from '~/api'

import { useChallengeTestRuntimePanel } from './useChallengeTestRuntimePanel'
import View from '~/components/views/admin/ChallengeTestRuntimePanelView.vue'

const props = defineProps<{
  challengeId: string
  definitionDirty: boolean
}>()
const state = bindViewState(useChallengeTestRuntimePanel(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
