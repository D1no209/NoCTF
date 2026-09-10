<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAdminPlatformUsersPage } from './useAdminPlatformUsersPage'
import View from '~/components/views/page/admin/platform/AdminPlatformUsersPageView.vue'

const state = bindViewState(useAdminPlatformUsersPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
