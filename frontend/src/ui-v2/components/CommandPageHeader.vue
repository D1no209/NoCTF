<script setup lang="ts">
import type { Component } from 'vue'
import CommandSignal from '../primitives/CommandSignal.vue'

withDefaults(defineProps<{
  signalLabel: string
  signalTone?: 'info' | 'success' | 'warning' | 'danger'
  title: string
  description?: string
  statIcon?: Component
  statValue?: string
  statLabel?: string
  statText?: boolean
}>(), {
  signalTone: 'success',
  description: undefined,
  statIcon: undefined,
  statValue: undefined,
  statLabel: undefined,
  statText: false,
})
</script>

<template>
  <header class="command-page-header">
    <div class="command-page-header__body">
      <CommandSignal :label="signalLabel" :tone="signalTone" />
      <h1>{{ title }}</h1>
      <p v-if="description">{{ description }}</p>
    </div>

    <div v-if="statValue !== undefined || $slots.default" class="command-page-header__aside">
      <div
        v-if="statValue !== undefined"
        class="command-page-header__stat"
        :class="{ 'command-page-header__stat--text': statText }"
      >
        <span v-if="statIcon" class="command-page-header__stat-icon">
          <component :is="statIcon" class="size-4" />
        </span>
        <strong>{{ statValue }}</strong>
        <span v-if="statLabel" class="command-page-header__stat-label">{{ statLabel }}</span>
      </div>
      <div v-if="$slots.default" class="command-page-header__actions">
        <slot />
      </div>
    </div>
  </header>
</template>

<style scoped>
.command-page-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
  margin-bottom: 22px;
  padding: 2px 4px 0;
}

.command-page-header__body { min-width: 0; }

.command-page-header__body h1 {
  margin: 10px 0 0;
  color: var(--v2-text);
  font-size: 26px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.command-page-header__body p {
  max-width: 68ch;
  margin: 8px 0 0;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.6;
}

.command-page-header__aside {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 10px;
}

.command-page-header__stat {
  display: grid;
  grid-template-columns: auto auto;
  align-items: center;
  column-gap: 11px;
  border-radius: 14px;
  padding: 9px 16px 9px 9px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}

.command-page-header__stat-icon {
  display: grid;
  width: 34px;
  height: 34px;
  grid-row: span 2;
  place-items: center;
  border-radius: 999px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
}

.command-page-header__stat strong {
  color: var(--v2-text);
  font-family: var(--v2-font-mono);
  font-size: 21px;
  line-height: 1;
}

.command-page-header__stat--text strong {
  max-width: 190px;
  overflow: hidden;
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.command-page-header__stat-label {
  margin-top: 4px;
  color: var(--v2-text-muted);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.04em;
}

.command-page-header__actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

@media (max-width: 680px) {
  .command-page-header {
    align-items: stretch;
    flex-direction: column;
    gap: 14px;
  }

  .command-page-header__aside {
    align-items: stretch;
  }

  .command-page-header__stat { width: fit-content; }
}
</style>
