<script setup lang="ts">
import { TabsRoot } from 'reka-ui'

const modelValue = defineModel<string>()
</script>

<template>
  <TabsRoot v-model="modelValue" data-slot="tabs">
    <slot />
  </TabsRoot>
</template>
