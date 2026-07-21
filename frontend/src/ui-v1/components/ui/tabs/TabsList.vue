<script setup lang="ts">
import { TabsList as TabsListPrimitive } from 'reka-ui'
</script>

<template>
  <TabsListPrimitive class="inline-flex h-9 items-center justify-center rounded-md bg-muted p-1 text-muted-foreground">
    <slot />
  </TabsListPrimitive>
</template>
