<script setup lang="ts">
const props = withDefaults(defineProps<{
  label: string
  tone?: 'primary' | 'outline' | 'ghost'
  type?: 'button' | 'submit'
  disabled?: boolean
}>(), {
  tone: 'primary',
  type: 'button',
  disabled: false,
})

defineEmits<{
  click: []
}>()
</script>

<template>
  <button
    :type="props.type"
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
  height: 38px;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border: 0;
  border-radius: 12px;
  padding: 0 16px;
  background: var(--v2-surface);
  box-shadow: var(--v2-raised);
  color: var(--v2-text);
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.01em;
  transition: box-shadow 200ms ease, color 200ms ease, background-color 200ms ease;
}

.command-button:enabled { cursor: pointer; }
.command-button:disabled { cursor: not-allowed; opacity: 0.52; }
.command-button:hover:enabled { box-shadow: var(--v2-raised-hover); }
.command-button:active:enabled { box-shadow: var(--v2-inset); }

.command-button--primary {
  background: var(--v2-primary);
  color: #ffffff;
  font-weight: 600;
}

.command-button--primary:active:enabled {
  box-shadow: inset 4px 4px 8px rgb(0 0 0 / 0.28), inset -4px -4px 8px rgb(255 255 255 / 0.22);
}

.command-button--outline {
  color: var(--v2-primary);
}

.command-button--ghost {
  background: transparent;
  box-shadow: none;
  color: var(--v2-text-muted);
}

.command-button--ghost:hover:enabled {
  background: var(--v2-surface);
  box-shadow: var(--v2-raised-sm);
  color: var(--v2-text);
}

.command-button--ghost:active:enabled { box-shadow: var(--v2-inset); }
</style>
