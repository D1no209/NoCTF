<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAdminPlatformVideoPage } from './useAdminPlatformVideoPage'
import View from '~/components/views/page/admin/platform/AdminPlatformVideoPageView.vue'
const state = bindViewState(useAdminPlatformVideoPage())
</script>
<template><View :state="state" /></template>
