<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useApplicationRoot } from './useApplicationRoot'
import View from '~/components/views/app/ApplicationRootView.vue'

const state = bindViewState(useApplicationRoot())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
