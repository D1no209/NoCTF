<script setup lang="ts">
const props = withDefaults(defineProps<{
  modelValue: string
  placeholder?: string
  label: string
  rows?: number
  mono?: boolean
  disabled?: boolean
}>(), {
  placeholder: '',
  rows: 4,
  mono: false,
  disabled: false,
})

defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <textarea
    :value="props.modelValue"
    class="command-textarea"
    :class="{ 'command-textarea--mono': props.mono }"
    :rows="props.rows"
    :aria-label="props.label"
    :placeholder="props.placeholder"
    :disabled="props.disabled"
    @input="$emit('update:modelValue', ($event.target as HTMLTextAreaElement).value)"
  />
</template>

<style scoped>
.command-textarea {
  width: 100%;
  min-width: 0;
  border: 0;
  border-radius: 12px;
  outline: 0;
  padding: 10px 14px;
  color: var(--v2-text);
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  font-family: inherit;
  font-size: 13px;
  line-height: 1.55;
  resize: vertical;
  transition: box-shadow 200ms ease;
}

.command-textarea--mono { font-family: var(--v2-font-mono); font-size: 12px; }
.command-textarea::placeholder { color: var(--v2-text-faint); }
.command-textarea:focus { box-shadow: var(--v2-inset-strong); }
.command-textarea:disabled { cursor: not-allowed; opacity: 0.52; }
</style>
