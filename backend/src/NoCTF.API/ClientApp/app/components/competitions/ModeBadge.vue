<script setup lang="ts">
import { gameModeLabel } from '~/utils/labels'

const props = defineProps<{ mode?: string }>()

const label = computed(() => gameModeLabel(props.mode))
</script>

<template>
  <Badge variant="outline" class="font-mono">{{ label }}</Badge>
</template>
