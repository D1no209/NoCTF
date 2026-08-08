<script setup lang="ts">
withDefaults(defineProps<{
  modelValue: number | null
  min?: number
  max?: number
  step?: number | string
  placeholder?: string
  disabled?: boolean
  id?: string
}>(), {
  min: undefined,
  max: undefined,
  step: 1,
  placeholder: undefined,
  disabled: false,
  id: undefined,
})

const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

function onInput(event: Event): void {
  const raw = (event.target as HTMLInputElement).value
  if (raw === '') {
    emit('update:modelValue', null)
    return
  }
  const value = Number(raw)
  if (Number.isFinite(value)) emit('update:modelValue', value)
}
</script>

<template>
  <Input
    :id="id"
    type="number"
    :model-value="modelValue === null ? '' : String(modelValue)"
    :min="min"
    :max="max"
    :step="step"
    :placeholder="placeholder"
    :disabled="disabled"
    @input="onInput"
  />
</template>
