<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminPlatformIndexPage } from './useAdminPlatformIndexPage'
import View from '~/components/views/page/admin/platform/AdminPlatformIndexPageView.vue'

const state = bindViewState(useAdminPlatformIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
