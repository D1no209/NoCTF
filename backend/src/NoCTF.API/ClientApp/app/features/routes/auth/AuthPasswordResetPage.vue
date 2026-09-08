<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAuthPasswordResetPage } from './useAuthPasswordResetPage'
import View from '~/components/views/page/auth/AuthPasswordResetPageView.vue'

const state = bindViewState(useAuthPasswordResetPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
