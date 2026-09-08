<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse } from '~/api'
import { useAdminPlatformBotsPage } from './useAdminPlatformBotsPage'
import View from '~/components/views/page/admin/platform/AdminPlatformBotsPageView.vue'

const state = bindViewState(useAdminPlatformBotsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
