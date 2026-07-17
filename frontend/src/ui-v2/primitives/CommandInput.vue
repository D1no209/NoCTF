<script setup lang="ts">
const props = withDefaults(defineProps<{
  modelValue: string
  placeholder?: string
  label: string
  type?: 'search' | 'text'
}>(), {
  placeholder: '',
  type: 'search',
})

defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <input
    :value="props.modelValue"
    class="command-input"
    :type="props.type"
    :aria-label="props.label"
    :placeholder="props.placeholder"
    @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
  >
</template>

<style scoped>
.command-input {
  width: 100%;
  min-width: 0;
  height: 38px;
  border: 0;
  border-radius: 12px;
  outline: 0;
  padding: 0 14px;
  color: var(--v2-text);
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  font-size: 13px;
  transition: box-shadow 200ms ease;
}

.command-input::placeholder { color: var(--v2-text-faint); }
.command-input:focus { box-shadow: var(--v2-inset-strong); }
</style>
