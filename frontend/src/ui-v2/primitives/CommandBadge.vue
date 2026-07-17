<script setup lang="ts">
const props = withDefaults(defineProps<{
  label: string
  tone?: 'default' | 'primary' | 'info' | 'success' | 'warning' | 'danger'
}>(), {
  tone: 'default',
})
</script>

<template>
  <span class="command-badge" :class="`command-badge--${props.tone}`">
    {{ props.label }}
  </span>
</template>

<style scoped>
.command-badge {
  display: inline-flex;
  align-items: center;
  width: fit-content;
  border-radius: 999px;
  padding: 4px 10px;
  background: var(--v2-surface);
  box-shadow: var(--v2-raised-sm);
  color: var(--v2-text-muted);
  font-family: var(--v2-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.04em;
}

.command-badge--primary { color: var(--v2-primary); }
.command-badge--info { color: var(--v2-info); }
.command-badge--success { color: var(--v2-cyan); }
.command-badge--warning { color: var(--v2-warning); }
.command-badge--danger { color: var(--v2-danger); }
</style>
