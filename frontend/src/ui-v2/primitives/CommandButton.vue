<script setup lang="ts">
const props = withDefaults(defineProps<{
  label: string
  tone?: 'primary' | 'outline' | 'ghost'
  disabled?: boolean
}>(), {
  tone: 'primary',
  disabled: false,
})

defineEmits<{
  click: []
}>()
</script>

<template>
  <button
    type="button"
    class="command-button"
    :class="`command-button--${props.tone}`"
    :disabled="props.disabled"
    @click="$emit('click')"
  >
    <span>{{ props.label }}</span>
    <slot name="icon" />
  </button>
</template>

<style scoped>
.command-button {
  display: inline-flex;
  min-width: 0;
  height: 34px;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border: 1px solid transparent;
  padding: 0 12px;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0;
  transition: color 120ms ease, background-color 120ms ease, border-color 120ms ease;
}

.command-button:enabled { cursor: pointer; }
.command-button:disabled { cursor: not-allowed; opacity: 0.52; }
.command-button--primary { border-color: var(--v2-primary); color: var(--v2-canvas); background: var(--v2-primary); }
.command-button--primary:hover:enabled { border-color: var(--v2-cyan); background: var(--v2-cyan); }
.command-button--outline { border-color: var(--v2-line-bright); color: var(--v2-text); background: transparent; }
.command-button--outline:hover:enabled { background: var(--v2-surface-hover); }
.command-button--ghost { color: var(--v2-text-muted); background: transparent; }
.command-button--ghost:hover:enabled { color: var(--v2-text); background: var(--v2-surface-hover); }
</style>
