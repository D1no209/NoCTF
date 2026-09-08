<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useDefaultLayout } from './useDefaultLayout'
import View from '~/components/views/layout/DefaultLayoutView.vue'

const state = bindViewState(useDefaultLayout())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
