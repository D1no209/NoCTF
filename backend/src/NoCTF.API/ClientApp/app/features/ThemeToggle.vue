<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useThemeToggle } from './useThemeToggle'
import View from '~/components/views/ThemeToggleView.vue'

const state = bindViewState(useThemeToggle())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
