<script setup lang="ts">
import { TabsTrigger as TabsTriggerPrimitive } from 'reka-ui'

const props = defineProps<{
  value: string
}>()
</script>

<template>
  <TabsTriggerPrimitive
    :value="props.value"
    class="inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1 text-sm font-medium transition-all disabled:pointer-events-none disabled:opacity-50 data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm hover:text-foreground"
  >
    <slot />
  </TabsTriggerPrimitive>
</template>
