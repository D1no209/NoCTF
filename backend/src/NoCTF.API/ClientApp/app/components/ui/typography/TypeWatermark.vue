<script setup lang="ts">
withDefaults(defineProps<{ text: string; size?: 'default' | 'compact'; placement?: 'center' | 'top' }>(), { size: 'default', placement: 'center' })
</script>

<template>
  <span data-slot="type-watermark" :data-size="size" :data-placement="placement" aria-hidden="true">{{ text }}</span>
</template>
