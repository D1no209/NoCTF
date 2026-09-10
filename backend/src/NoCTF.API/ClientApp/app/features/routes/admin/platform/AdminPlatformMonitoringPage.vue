<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminPlatformMonitoringPage } from './useAdminPlatformMonitoringPage'
import View from '~/components/views/page/admin/platform/AdminPlatformMonitoringPageView.vue'

const state = bindViewState(useAdminPlatformMonitoringPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
