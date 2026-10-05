<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '~/api'
import { useGameModeBadge } from './useGameModeBadge'
import View from '~/components/views/admin/GameModeBadgeView.vue'

const props = defineProps<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null }>()
const state = bindViewState(useGameModeBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
