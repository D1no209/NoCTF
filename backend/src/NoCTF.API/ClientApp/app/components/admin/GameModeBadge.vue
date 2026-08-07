<script setup lang="ts">
const props = defineProps<{ mode?: number | null }>()

const label = computed(() => enumLabel(GameModeLabel, props.mode))
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
