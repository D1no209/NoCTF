<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminGameModeBadge } from './useAdminGameModeBadge'
import View from '~/components/views/admin/AdminGameModeBadgeView.vue'

const props = defineProps<{ mode?: number | string | null }>()
const state = bindViewState(useAdminGameModeBadge(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
