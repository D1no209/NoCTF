<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import View from '~/components/views/page/admin/platform/AdminPlatformExperimentsPageView.vue'
import { useAdminPlatformExperimentsPage } from './useAdminPlatformExperimentsPage'

const state = bindViewState(useAdminPlatformExperimentsPage())
</script>

<template>
  <View :state="state" />
</template>
