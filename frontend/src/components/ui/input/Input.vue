<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { InputModelModifiers } from './modelValue'
import { useVModel } from '@vueuse/core'
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { applyInputModelModifiers } from './modelValue'

const props = defineProps<{
  defaultValue?: string | number
  modelValue?: string | number
  modelModifiers?: InputModelModifiers
  class?: HTMLAttributes['class']
}>()

const emits = defineEmits<{
  (e: 'update:modelValue', payload: string | number): void
}>()

const modelValue = useVModel(props, 'modelValue', emits, {
  passive: true,
  defaultValue: props.defaultValue,
})

const inputValue = computed({
  get: () => modelValue.value,
  set: (value: string | number) => {
    modelValue.value = applyInputModelModifiers(value, props.modelModifiers)
  },
})
</script>

<template>
  <input v-model="inputValue" :class="cn('flex h-10 w-full border-2 border-input bg-card px-3 py-1 text-sm leading-6 transition-colors file:border-0 file:bg-transparent file:text-foreground file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:border-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/20 focus-visible:ring-offset-0 disabled:cursor-not-allowed disabled:opacity-50', props.class)">
</template>
