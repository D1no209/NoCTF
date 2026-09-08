<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useUsersByIdPage } from './useUsersByIdPage'
import View from '~/components/views/page/users/UsersByIdPageView.vue'

const state = bindViewState(useUsersByIdPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
