<script setup lang="ts">
const props = withDefaults(defineProps<{
  checked: boolean
  label: string
  description?: string
  disabled?: boolean
}>(), {
  description: undefined,
  disabled: false,
})

const emit = defineEmits<{
  'update:checked': [value: boolean]
}>()

function toggle() {
  if (!props.disabled)
    emit('update:checked', !props.checked)
}
</script>

<template>
  <button
    type="button"
    class="command-checkbox"
    :class="{ 'command-checkbox--on': props.checked }"
    role="checkbox"
    :aria-checked="props.checked"
    :disabled="props.disabled"
    @click="toggle"
  >
    <span class="command-checkbox__box" aria-hidden="true">
      <svg v-if="props.checked" viewBox="0 0 12 12" class="command-checkbox__mark">
        <path d="M2 6.2 4.8 9 10 3.4" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </span>
    <span class="command-checkbox__text">
      <span class="command-checkbox__label">{{ props.label }}</span>
      <span v-if="props.description" class="command-checkbox__description">{{ props.description }}</span>
    </span>
  </button>
</template>

<style scoped>
.command-checkbox {
  display: flex;
  width: 100%;
  min-width: 0;
  align-items: center;
  gap: 11px;
  border: 0;
  border-radius: 12px;
  padding: 10px 12px;
  background: var(--v2-surface);
  box-shadow: var(--v2-raised-sm);
  color: var(--v2-text);
  cursor: pointer;
  font-family: inherit;
  text-align: left;
  transition: box-shadow 160ms ease;
}

.command-checkbox:disabled { cursor: not-allowed; opacity: 0.52; }

.command-checkbox__box {
  display: grid;
  width: 20px;
  height: 20px;
  flex: none;
  place-items: center;
  border-radius: 7px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
}

.command-checkbox--on .command-checkbox__box { box-shadow: var(--v2-inset-strong); }
.command-checkbox__mark { width: 12px; height: 12px; }

.command-checkbox__text { display: grid; min-width: 0; gap: 2px; }
.command-checkbox__label { color: var(--v2-text); font-size: 13px; font-weight: 600; }
.command-checkbox__description { color: var(--v2-text-muted); font-size: 11px; line-height: 1.45; }
</style>
