<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAuthVerifyEmailPage } from './useAuthVerifyEmailPage'
import View from '~/components/views/page/auth/AuthVerifyEmailPageView.vue'

const state = bindViewState(useAuthVerifyEmailPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
