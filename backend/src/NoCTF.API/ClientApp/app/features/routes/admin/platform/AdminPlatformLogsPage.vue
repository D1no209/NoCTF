<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminPlatformLogsPage } from './useAdminPlatformLogsPage'
import View from '~/components/views/page/admin/platform/AdminPlatformLogsPageView.vue'

const state = bindViewState(useAdminPlatformLogsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
