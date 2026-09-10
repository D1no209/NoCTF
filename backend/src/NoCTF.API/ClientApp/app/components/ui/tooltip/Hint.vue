<script setup lang="ts">
defineProps<{ content?: string | null }>()
</script>

<template>
  <Tooltip v-if="content">
    <TooltipTrigger as-child><slot /></TooltipTrigger>
    <TooltipContent :side-offset="6">{{ content }}</TooltipContent>
  </Tooltip>
  <slot v-else />
</template>
