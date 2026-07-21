<script setup lang="ts">
import { ref } from 'vue'
import { Upload } from 'lucide-vue-next'

const props = withDefaults(defineProps<{
  label: string
  accept?: string
  fileName?: string
  disabled?: boolean
}>(), {
  accept: undefined,
  fileName: undefined,
  disabled: false,
})

const emit = defineEmits<{
  'update:file': [file: File | null]
}>()

const inputEl = ref<HTMLInputElement | null>(null)

function onChange(event: Event) {
  const input = event.target as HTMLInputElement
  emit('update:file', input.files?.[0] ?? null)
}
</script>

<template>
  <label class="command-file-input" :class="{ 'command-file-input--disabled': props.disabled }">
    <Upload class="size-4" />
    <span class="command-file-input__name">{{ props.fileName || props.label }}</span>
    <input
      ref="inputEl"
      type="file"
      class="command-file-input__native"
      :aria-label="props.label"
      :accept="props.accept"
      :disabled="props.disabled"
      @change="onChange"
    >
  </label>
</template>

<style scoped>
.command-file-input {
  display: flex;
  width: 100%;
  min-width: 0;
  height: 38px;
  align-items: center;
  gap: 10px;
  border-radius: 12px;
  padding: 0 14px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-text-muted);
  cursor: pointer;
  font-size: 12px;
  transition: box-shadow 200ms ease;
}

.command-file-input:hover { box-shadow: var(--v2-inset-strong); }
.command-file-input--disabled { cursor: not-allowed; opacity: 0.52; }
.command-file-input__name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.command-file-input__native { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
