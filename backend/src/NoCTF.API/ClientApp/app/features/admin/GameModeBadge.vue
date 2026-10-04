<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol } from '~/api/models'
import { useGameModeBadge } from './useGameModeBadge'
import View from '~/components/views/admin/GameModeBadgeView.vue'

const props = defineProps<{ mode?: NoCTFAPIEndpointsCompetitionsGameModeProtocol | null }>()
const state = bindViewState(useGameModeBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
