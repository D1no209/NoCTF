<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminDateTime } from './useAdminDateTime'
import View from '~/components/views/admin/AdminDateTimeView.vue'

const props = defineProps<{ value?: string | null }>()
const state = bindViewState(useAdminDateTime(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
