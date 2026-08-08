<script setup lang="ts">
const props = defineProps<{ mode?: string | null }>()

const label = computed(() => enumLabel(GameModeLabel, props.mode))
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
