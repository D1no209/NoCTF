<script setup lang="ts">
import type { StatusIconName } from './status-icons'
import { motionAttributes } from '~/motion/presets'

defineProps<{ name?: StatusIconName | null; label?: string | null }>()
</script>

<template>
  <span v-if="name && label" v-bind="motionAttributes('status-mark')" data-slot="status-icon" :data-status-icon="name" role="img" :aria-label="label">
    <span data-slot="status-icon-glyph" aria-hidden="true" />
  </span>
</template>
