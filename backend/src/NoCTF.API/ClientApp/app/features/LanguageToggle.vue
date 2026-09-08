<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useLanguageToggle } from './useLanguageToggle'
import View from '~/components/views/LanguageToggleView.vue'

const state = bindViewState(useLanguageToggle())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
