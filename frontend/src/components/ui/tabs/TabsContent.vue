<script setup lang="ts">
import { inject } from 'vue'

const props = defineProps<{
  value: string
}>()

const activeValue = inject('tabs-value', { value: undefined as string | undefined })
</script>

<template>
  <div v-if="activeValue?.value === props.value" class="mt-2">
    <slot />
  </div>
</template>
