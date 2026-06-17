<script setup lang="ts">
import { computed, provide } from 'vue'

const modelValue = defineModel<string>()

const activeValue = computed(() => modelValue.value)
provide('tabs-value', activeValue)
provide('tabs-set-value', (value: string) => {
  modelValue.value = value
})
</script>

<template>
  <div data-slot="tabs">
    <slot />
  </div>
</template>
