<script setup lang="ts">
import type { ThemePackage } from '@/themes/theme-package'
import { Check, Layers3, Paintbrush, RadioTower } from 'lucide-vue-next'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  themePackages: ThemePackage[]
  activeThemeId: string
}>()

const emit = defineEmits<{
  apply: [id: string]
}>()

function isActive(theme: ThemePackage) {
  return theme.id === props.activeThemeId
}

function handleApply(theme: ThemePackage) {
  emit('apply', theme.id)
}
</script>

<template>
  <section class="theme-selector" aria-label="Theme packages">
    <div class="theme-selector__summary">
      <div>
        <CommandSignal label="Global presentation package" tone="info" />
        <h2>Available packages</h2>
      </div>
      <span>{{ themePackages.length }} loaded</span>
    </div>

    <div class="theme-selector__grid">
      <CommandPanel
        v-for="theme in themePackages"
        :key="theme.id"
        class="theme-selector__package"
        :tone="isActive(theme) ? 'signal' : 'default'"
      >
        <div
          class="theme-selector__preview"
          :style="{ background: theme.preview.background }"
          aria-hidden="true"
        >
          <div class="theme-selector__preview-top">
            <span :style="{ background: theme.preview.surface }" />
            <span :style="{ background: theme.preview.primary }" />
          </div>
          <div class="theme-selector__preview-layout">
            <i :style="{ background: theme.preview.primary }" />
            <div>
              <b :style="{ background: theme.preview.surface }" />
              <b :style="{ background: theme.preview.accent }" />
            </div>
          </div>
          <div class="theme-selector__preview-signal" :style="{ color: theme.preview.primary }">
            <RadioTower class="size-3.5" />
            <span>THEME LINK</span>
          </div>
        </div>

        <div class="theme-selector__content">
          <header class="theme-selector__header">
            <div>
              <h3>{{ theme.name }}</h3>
              <span>v{{ theme.version }}</span>
            </div>
            <CommandSignal
              :label="isActive(theme) ? 'Active' : theme.builtIn ? 'Built in' : 'Local'"
              :tone="isActive(theme) ? 'success' : 'info'"
            />
          </header>

          <p>{{ theme.description || 'No package description provided.' }}</p>

          <footer class="theme-selector__footer">
            <span>
              <Layers3 class="size-3.5" />
              {{ theme.builtIn ? 'Verified base package' : 'Local package' }}
            </span>
            <CommandIconButton
              :icon="isActive(theme) ? Check : Paintbrush"
              :label="isActive(theme) ? 'Currently active' : 'Apply package'"
              :active="isActive(theme)"
              @click="handleApply(theme)"
            />
          </footer>
        </div>
      </CommandPanel>
    </div>
  </section>
</template>

<style scoped>
.theme-selector {
  display: grid;
  gap: 16px;
}

.theme-selector__summary {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 16px;
  padding: 0 2px;
}

.theme-selector__summary h2 {
  margin: 7px 0 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
}

.theme-selector__summary > span {
  color: var(--v2-text-muted);
  font-family: var(--v2-font-mono);
  font-size: 11px;
  font-weight: 600;
}

.theme-selector__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.theme-selector__package {
  display: grid;
  min-height: 310px;
  grid-template-rows: auto 1fr;
  gap: 0;
  padding: 12px;
}

.theme-selector__preview {
  position: relative;
  overflow: hidden;
  min-height: 122px;
  border-radius: 12px;
  padding: 12px;
  box-shadow: var(--v2-inset);
}

.theme-selector__preview-top,
.theme-selector__preview-layout,
.theme-selector__preview-signal {
  position: relative;
  z-index: 1;
}

.theme-selector__preview-top {
  display: flex;
  gap: 6px;
}

.theme-selector__preview-top span {
  display: block;
  width: 38px;
  height: 6px;
  border-radius: 999px;
}

.theme-selector__preview-layout {
  display: grid;
  grid-template-columns: 44px 1fr;
  gap: 8px;
  margin-top: 20px;
}

.theme-selector__preview-layout > i {
  display: block;
  min-height: 58px;
  border-radius: 10px;
  opacity: 0.9;
}

.theme-selector__preview-layout > div {
  display: grid;
  grid-template-rows: 36px 14px;
  gap: 8px;
}

.theme-selector__preview-layout b {
  display: block;
  min-width: 0;
  border-radius: 8px;
  opacity: 0.92;
}

.theme-selector__preview-signal {
  position: absolute;
  right: 12px;
  bottom: 10px;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-family: var(--v2-font-mono);
  font-size: 8px;
  font-weight: 600;
  letter-spacing: 0.06em;
}

.theme-selector__content {
  display: grid;
  min-height: 0;
  grid-template-rows: auto 1fr auto;
  gap: 14px;
  padding: 14px 4px 2px;
}

.theme-selector__header,
.theme-selector__footer,
.theme-selector__footer > span {
  display: flex;
  align-items: center;
}

.theme-selector__header,
.theme-selector__footer {
  justify-content: space-between;
  gap: 12px;
}

.theme-selector__header h3 {
  margin: 0;
  color: var(--v2-text);
  font-family: var(--v2-font-mono);
  font-size: 13px;
  font-weight: 600;
}

.theme-selector__header > div > span,
.theme-selector__footer > span {
  color: var(--v2-text-muted);
  font-size: 11px;
}

.theme-selector__header > div > span {
  display: block;
  margin-top: 4px;
  font-family: var(--v2-font-mono);
}

.theme-selector__content > p {
  margin: 0;
  color: var(--v2-text-muted);
  font-size: 12px;
  line-height: 1.55;
}

.theme-selector__footer > span {
  gap: 6px;
  min-width: 0;
}

.theme-selector__footer :deep(.command-icon-button) {
  flex: none;
}

@media (max-width: 1120px) {
  .theme-selector__grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 640px) {
  .theme-selector__summary {
    align-items: flex-start;
    flex-direction: column;
  }

  .theme-selector__grid {
    grid-template-columns: 1fr;
  }
}
</style>
