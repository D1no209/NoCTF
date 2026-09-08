<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAuthRegisterPage } from './useAuthRegisterPage'
import View from '~/components/views/page/auth/AuthRegisterPageView.vue'

const state = bindViewState(useAuthRegisterPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
