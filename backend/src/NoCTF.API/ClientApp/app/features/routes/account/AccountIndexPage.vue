<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAccountIndexPage } from './useAccountIndexPage'
import View from '~/components/views/page/account/AccountIndexPageView.vue'

const state = bindViewState(useAccountIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
