<script setup lang="ts">
import type { Component } from 'vue'

const props = withDefaults(defineProps<{
  icon: Component
  label: string
  active?: boolean
  compact?: boolean
}>(), {
  active: false,
  compact: false,
})

defineEmits<{
  click: []
}>()
</script>

<template>
  <button
    type="button"
    class="command-icon-button"
    :class="{ 'command-icon-button--active': props.active, 'command-icon-button--compact': props.compact }"
    :title="props.label"
    :aria-label="props.label"
    @click="$emit('click')"
  >
    <component :is="props.icon" class="size-4 shrink-0" />
    <span v-if="!props.compact" class="command-icon-button__label">{{ props.label }}</span>
  </button>
</template>

<style scoped>
.command-icon-button {
  display: inline-flex;
  min-width: 0;
  height: 38px;
  align-items: center;
  gap: 9px;
  border: 0;
  border-radius: 12px;
  padding: 0 12px;
  color: var(--v2-text-muted);
  background: transparent;
  cursor: pointer;
  transition: box-shadow 200ms ease, color 200ms ease, background-color 200ms ease;
}

.command-icon-button:hover {
  background: var(--v2-surface);
  box-shadow: var(--v2-raised-sm);
  color: var(--v2-text);
}

.command-icon-button:active { box-shadow: var(--v2-inset); }

.command-icon-button--active,
.command-icon-button--active:hover {
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
}

.command-icon-button--compact {
  width: 38px;
  justify-content: center;
  padding: 0;
}

.command-icon-button__label {
  overflow: hidden;
  font-size: 12px;
  font-weight: 600;
  line-height: 1;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
