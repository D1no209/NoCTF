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
  height: 34px;
  align-items: center;
  gap: 9px;
  border: 1px solid transparent;
  padding: 0 10px;
  color: var(--v2-text-muted);
  background: transparent;
  cursor: pointer;
  transition: color 120ms ease, background-color 120ms ease, border-color 120ms ease;
}

.command-icon-button:hover {
  border-color: var(--v2-line);
  color: var(--v2-text);
  background: var(--v2-surface-hover);
}

.command-icon-button--active {
  border-color: var(--v2-line-bright);
  color: var(--v2-cyan);
  background: rgb(47 140 255 / 0.12);
}

.command-icon-button--compact {
  width: 34px;
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
