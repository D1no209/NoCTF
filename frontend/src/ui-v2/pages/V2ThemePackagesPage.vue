<script setup lang="ts">
import { Command, MonitorCog } from 'lucide-vue-next'
import { computed } from 'vue'
import { useThemePackages } from '@/composables/useThemePackages'
import CommandThemePackageSelector from '../components/CommandThemePackageSelector.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const { activeTheme, activeThemeId, applyTheme, themePackages } = useThemePackages()

const activeThemeName = computed(() => activeTheme.value.name)

function handleApply(id: string) {
  applyTheme(id)
}
</script>

<template>
  <section class="v2-theme-packages">
    <header class="v2-theme-packages__heading">
      <div>
        <CommandSignal label="System configuration / visual runtime" tone="success" />
        <h1>Global theme packages</h1>
        <p>
          Choose the active package for the complete NoCTF interface. The Neon Command workspace
          remains operational while the global package is changed.
        </p>
      </div>
      <div class="v2-theme-packages__active">
        <MonitorCog class="size-4" />
        <div>
          <span>Current global package</span>
          <strong>{{ activeThemeName }}</strong>
        </div>
        <Command class="size-4" />
      </div>
    </header>

    <CommandThemePackageSelector
      :theme-packages="themePackages"
      :active-theme-id="activeThemeId"
      @apply="handleApply"
    />
  </section>
</template>

<style scoped>
.v2-theme-packages {
  display: grid;
  gap: 18px;
}

.v2-theme-packages__heading {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 24px;
  padding: 4px 2px 0;
}

.v2-theme-packages__heading h1 {
  margin: 8px 0 0;
  color: var(--v2-text);
  font-size: 24px;
  font-weight: 600;
  letter-spacing: 0;
}

.v2-theme-packages__heading p {
  max-width: 760px;
  margin: 8px 0 0;
  color: var(--v2-text-muted);
  font-size: 13px;
  line-height: 1.55;
}

.v2-theme-packages__active {
  display: flex;
  min-width: 255px;
  align-items: center;
  gap: 10px;
  border-radius: 14px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  padding: 12px 14px;
  color: var(--v2-primary);
}

.v2-theme-packages__active > div {
  display: grid;
  min-width: 0;
  gap: 3px;
}

.v2-theme-packages__active span {
  color: var(--v2-text-muted);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.05em;
}

.v2-theme-packages__active strong {
  overflow: hidden;
  color: var(--v2-text);
  font-family: var(--v2-font-mono);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.v2-theme-packages__active > :last-child {
  margin-left: auto;
  color: var(--v2-cyan);
}

@media (max-width: 760px) {
  .v2-theme-packages__heading {
    align-items: flex-start;
    flex-direction: column;
  }

  .v2-theme-packages__active {
    width: 100%;
  }
}
</style>
