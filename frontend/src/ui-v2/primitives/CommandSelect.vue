<script setup lang="ts">
export interface CommandSelectOption {
  label: string
  value: string
}

const props = defineProps<{
  modelValue: string
  label: string
  options: CommandSelectOption[]
}>()

defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <select
    :value="props.modelValue"
    class="command-select"
    :aria-label="props.label"
    @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
  >
    <option v-for="option in props.options" :key="option.value" :value="option.value">
      {{ option.label }}
    </option>
  </select>
</template>

<style scoped>
.command-select {
  width: 100%;
  min-width: 0;
  height: 38px;
  border: 0;
  border-radius: 12px;
  outline: 0;
  padding: 0 30px 0 14px;
  color: var(--v2-text);
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  font-size: 13px;
  transition: box-shadow 200ms ease;
}

.command-select:focus { box-shadow: var(--v2-inset-strong); }
</style>
