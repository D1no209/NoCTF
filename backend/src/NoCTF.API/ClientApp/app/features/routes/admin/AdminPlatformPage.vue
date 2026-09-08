<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminPlatformPage } from './useAdminPlatformPage'
import View from '~/components/views/page/admin/AdminPlatformPageView.vue'

const state = bindViewState(useAdminPlatformPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
