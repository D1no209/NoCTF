<script setup lang="ts">
import { MonitorCog, Upload } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useThemePackages } from '@/composables/useThemePackages'
import CommandPageHeader from '../components/CommandPageHeader.vue'
import CommandThemePackageSelector from '../components/CommandThemePackageSelector.vue'
import CommandButton from '../primitives/CommandButton.vue'

const { activeTheme, activeThemeId, applyTheme, themePackages, importTheme, importThemeArchive } = useThemePackages()

const activeThemeName = computed(() => activeTheme.value.name)
const importInput = ref<HTMLInputElement>()
const importMessage = ref('')
const importTone = ref<'success' | 'danger'>('success')

function handleApply(id: string) {
  applyTheme(id)
}

function isZipArchive(file: File) {
  return file.name.toLowerCase().endsWith('.zip') || file.type.includes('zip')
}

function reportImport(message: string) {
  importTone.value = 'success'
  importMessage.value = message
}

function reportImportError(message: string) {
  importTone.value = 'danger'
  importMessage.value = message
}

async function handleImport(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file)
    return

  try {
    if (isZipArchive(file)) {
      const result = await importThemeArchive(await file.arrayBuffer())
      if (!result.imported.length) {
        const firstSkip = result.skipped[0]
        throw new Error(firstSkip
          ? `${firstSkip.name}: ${firstSkip.reason}`
          : 'The archive does not contain a compatible theme package.')
      }
      reportImport(`Imported ${result.imported.length} theme package${result.imported.length === 1 ? '' : 's'}${result.skipped.length ? `, skipped ${result.skipped.length} file${result.skipped.length === 1 ? '' : 's'}` : ''}.`)
    }
    else {
      const imported = importTheme(JSON.parse(await file.text()) as unknown)
      if (!imported)
        throw new Error('This file is not a compatible theme package.')
      reportImport(`${imported.name} imported.`)
    }
  }
  catch (error) {
    reportImportError(error instanceof Error ? error.message : 'Unable to import theme package.')
  }
  finally {
    input.value = ''
  }
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

    <div class="v2-theme-packages__actions">
      <input ref="importInput" class="v2-theme-packages__file" type="file" accept="application/json,.json,.theme.json,application/zip,.zip,application/x-zip-compressed" @change="handleImport" />
      <CommandButton label="Import package (JSON / ZIP)" tone="outline" @click="importInput?.click()">
        <template #icon><Upload class="size-4" /></template>
      </CommandButton>
    </div>

    <p v-if="importMessage" class="v2-theme-packages__message" :class="`v2-theme-packages__message--${importTone}`" role="status">
      {{ importMessage }}
    </p>

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

.v2-theme-packages__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.v2-theme-packages__file {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  clip-path: inset(50%);
  white-space: nowrap;
}

.v2-theme-packages__message {
  margin: 0;
  border-radius: 12px;
  padding: 12px 14px;
  color: var(--v2-cyan);
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  font-size: 13px;
}

.v2-theme-packages__message--danger {
  color: var(--v2-danger);
}
</style>
