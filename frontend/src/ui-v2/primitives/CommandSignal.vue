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
  gap: 6px;
  color: var(--v2-text-muted);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.command-signal i {
  width: 6px;
  height: 6px;
  background: var(--v2-primary);
  box-shadow: 0 0 10px currentColor;
}

.command-signal--info { color: var(--v2-primary); }
.command-signal--success { color: var(--v2-cyan); }
.command-signal--warning { color: var(--v2-warning); }
.command-signal--danger { color: var(--v2-danger); }
.command-signal--info i { background: var(--v2-primary); }
.command-signal--success i { background: var(--v2-cyan); }
.command-signal--warning i { background: var(--v2-warning); }
.command-signal--danger i { background: var(--v2-danger); }
</style>
