<script setup lang="ts">
import type { TechnicalIconName } from './technical-icons'

defineProps<{ name: TechnicalIconName }>()
</script>

<template>
  <span
    data-slot="technical-icon"
    :data-icon="name"
    class="size-5 shrink-0"
    aria-hidden="true"
  />
</template>
