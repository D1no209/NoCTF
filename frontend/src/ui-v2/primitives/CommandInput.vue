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
  height: 36px;
  border: 1px solid var(--v2-line);
  outline: 0;
  padding: 0 11px;
  color: var(--v2-text);
  background: rgb(5 8 22 / 0.48);
  font-size: 12px;
}

.command-input::placeholder { color: var(--v2-text-faint); }
.command-input:focus { border-color: var(--v2-primary); box-shadow: inset 2px 0 0 var(--v2-cyan); }
</style>
