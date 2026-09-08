<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse } from '~/api'
import { useAdminPlatformEmailPage } from './useAdminPlatformEmailPage'
import View from '~/components/views/page/admin/platform/AdminPlatformEmailPageView.vue'

const state = bindViewState(useAdminPlatformEmailPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
