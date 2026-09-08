<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse } from '~/api'

import { useAdminPlatformRuntimesPage } from './useAdminPlatformRuntimesPage'
import View from '~/components/views/page/admin/platform/AdminPlatformRuntimesPageView.vue'

const state = bindViewState(useAdminPlatformRuntimesPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
