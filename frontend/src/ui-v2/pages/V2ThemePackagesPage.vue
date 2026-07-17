<script setup lang="ts">
import { MonitorCog } from 'lucide-vue-next'
import { computed } from 'vue'
import { useThemePackages } from '@/composables/useThemePackages'
import CommandPageHeader from '../components/CommandPageHeader.vue'
import CommandThemePackageSelector from '../components/CommandThemePackageSelector.vue'

const { activeTheme, activeThemeId, applyTheme, themePackages } = useThemePackages()

const activeThemeName = computed(() => activeTheme.value.name)

function handleApply(id: string) {
  applyTheme(id)
}
</script>

<template>
  <section class="v2-theme-packages">
    <CommandPageHeader
      signal-label="System configuration / visual runtime"
      signal-tone="success"
      title="Global theme packages"
      description="Choose the active package for the complete NoCTF interface. The Neon Command workspace remains operational while the global package is changed."
      :stat-icon="MonitorCog"
      :stat-value="activeThemeName"
      stat-label="Current package"
      stat-text
    />

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
</style>
