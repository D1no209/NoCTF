<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse } from '~/api'

import { useAdminPlatformAuditPage } from './useAdminPlatformAuditPage'
import View from '~/components/views/page/admin/platform/AdminPlatformAuditPageView.vue'

const state = bindViewState(useAdminPlatformAuditPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
