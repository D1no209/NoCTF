<script setup lang="ts">
const props = withDefaults(defineProps<{
  tone?: 'info' | 'success' | 'warning' | 'danger'
  label: string
}>(), {
  tone: 'info',
})
</script>

<template>
  <span class="command-signal" :class="`command-signal--${props.tone}`">
    <i aria-hidden="true" />
    <span>{{ props.label }}</span>
  </span>
</template>

<style scoped>
.command-signal {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  width: fit-content;
  border-radius: 999px;
  padding: 4px 11px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-text-muted);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.command-signal i {
  width: 7px;
  height: 7px;
  flex: none;
  border-radius: 999px;
  background: var(--v2-info);
}

.command-signal--info { color: var(--v2-info); }
.command-signal--success { color: var(--v2-cyan); }
.command-signal--warning { color: var(--v2-warning); }
.command-signal--danger { color: var(--v2-danger); }
.command-signal--info i { background: var(--v2-info); }
.command-signal--success i { background: var(--v2-cyan); }
.command-signal--warning i { background: var(--v2-warning); }
.command-signal--danger i { background: var(--v2-danger); }
</style>
