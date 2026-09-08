<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '~/api'

import { useNotificationCenter } from './useNotificationCenter'
import View from '~/components/views/notifications/NotificationCenterView.vue'

const state = bindViewState(useNotificationCenter())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
