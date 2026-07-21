<script setup lang="ts">
import { TabsContent as TabsContentPrimitive } from 'reka-ui'

const props = defineProps<{
  value: string
}>()
</script>

<template>
  <TabsContentPrimitive :value="props.value" class="mt-2">
    <slot />
  </TabsContentPrimitive>
</template>
