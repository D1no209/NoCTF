<script setup lang="ts">
import type { TechnicalIconName } from '../icons/technical-icons'
import TechnicalIcon from '../icons/TechnicalIcon.vue'
import LucideIcon from '../icons/LucideIcon.vue'

withDefaults(defineProps<{ name: TechnicalIconName; icon?: string | null; size?: 'default' | 'compact' }>(), { size: 'default' })
</script>

<template>
  <span data-slot="icon-watermark" :data-size="size" aria-hidden="true">
    <LucideIcon v-if="icon" :name="icon" /><TechnicalIcon v-else :name="name" />
  </span>
</template>
