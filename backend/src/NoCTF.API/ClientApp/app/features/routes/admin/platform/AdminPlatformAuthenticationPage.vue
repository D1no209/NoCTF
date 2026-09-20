<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminPlatformAuthenticationPage } from './useAdminPlatformAuthenticationPage'
import View from '~/components/views/page/admin/platform/AdminPlatformAuthenticationPageView.vue'

const state = bindViewState(useAdminPlatformAuthenticationPage())
</script>

<template>
  <View :state="state" />
</template>
