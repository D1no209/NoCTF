<script setup lang="ts">
import type { TechnicalIconName } from '../icons/technical-icons'
import TechnicalIcon from '../icons/TechnicalIcon.vue'

withDefaults(defineProps<{ name: TechnicalIconName; size?: 'default' | 'compact' }>(), { size: 'default' })
</script>

<template>
  <span data-slot="icon-watermark" :data-size="size" aria-hidden="true">
    <TechnicalIcon :name="name" />
  </span>
</template>
