<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { usePrivateAccountPanel } from './usePrivateAccountPanel'
import View from '~/components/views/account/PrivateAccountPanelView.vue'

const props = withDefaults(defineProps<{ userId: string, competitionId?: string, teamId?: string, showActivities?: boolean }>(), { showActivities: true })
const state = bindViewState(usePrivateAccountPanel(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
