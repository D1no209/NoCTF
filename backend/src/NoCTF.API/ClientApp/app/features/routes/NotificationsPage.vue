<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useNotificationsPage } from './useNotificationsPage'
import View from '~/components/views/page/NotificationsPageView.vue'

const state = bindViewState(useNotificationsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
