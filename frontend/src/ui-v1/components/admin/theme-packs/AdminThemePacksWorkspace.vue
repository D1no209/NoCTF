<script setup lang="ts">
import { computed, ref } from 'vue'
import { Download, Palette, Plus, RotateCcw, Upload } from 'lucide-vue-next'
import { toast } from 'vue-sonner'
import ThemePackageCard from './ThemePackageCard.vue'
import ThemePackageDeleteDialog from './ThemePackageDeleteDialog.vue'
import ThemePackageEditorDialog from './ThemePackageEditorDialog.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent } from '@/ui-v1/components/ui/card'
import { Panel } from '@/ui-v1/components/ui/panel'
import { useThemePackages } from '@/composables/useThemePackages'
import { defaultThemeId } from '@/themes/presets'
import type { ThemePackage, ThemePackageDraft } from '@/themes/theme-package'

const {
  activeTheme,
  activeThemeId,
  themePackages,
  applyTheme,
  createTheme,
  duplicateTheme,
  updateTheme,
  removeTheme,
  importTheme,
  importThemeArchive,
  exportTheme,
} = useThemePackages()

const editorOpen = ref(false)
const editingTheme = ref<ThemePackage>()
const removingTheme = ref<ThemePackage>()
const importInput = ref<HTMLInputElement>()
const localThemeCount = computed(() => themePackages.value.filter(theme => !theme.builtIn).length)

function openCreateDialog() {
  editingTheme.value = undefined
  editorOpen.value = true
}

function handleApply(id: string) {
  const activation = applyTheme(id)
  if (!activation) {
    toast.error('Unable to apply this theme package.')
    return
  }
  if (!activation.persisted)
    toast.warning(`${activation.theme.name} applied for this session only`)
  else
    toast.success(`${activation.theme.name} applied`)
}

function handleDuplicate(id: string) {
  const theme = duplicateTheme(id)
  if (theme)
    toast.success(`${theme.name} added to this browser`)
  else
    toast.error('Unable to save the new theme package in this browser.')
}

function handleEdit(id: string) {
  const source = themePackages.value.find(theme => theme.id === id)
  if (!source)
    return

  if (source.builtIn) {
    const copy = duplicateTheme(id)
    if (!copy) {
      toast.error('Unable to save the local copy in this browser.')
      return
    }
    editingTheme.value = copy
    toast.message('Created a local copy for editing')
  }
  else {
    editingTheme.value = source
  }
  editorOpen.value = true
}

function handleSave(draft: ThemePackageDraft) {
  if (editingTheme.value) {
    const updated = updateTheme(editingTheme.value.id, draft)
    if (!updated) {
      toast.error('Unable to save the theme package in this browser.')
      return
    }
    toast.success('Theme package updated')
  }
  else {
    const created = createTheme(draft)
    if (!created) {
      toast.error('Unable to save the theme package in this browser.')
      return
    }
    const activation = applyTheme(created.id)
    if (activation?.persisted)
      toast.success(`${created.name} created and applied`)
    else
      toast.warning(`${created.name} created, but the active selection is only available for this session`)
  }
  editorOpen.value = false
}

function requestRemove(id: string) {
  const theme = themePackages.value.find(item => item.id === id)
  if (theme)
    removingTheme.value = theme
}

function handleRemove() {
  const theme = removingTheme.value
  if (!theme || !removeTheme(theme.id)) {
    toast.error('Unable to remove the theme package from this browser.')
    return
  }
  toast.success(`${theme.name} removed`)
  removingTheme.value = undefined
}

function exportActiveTheme() {
  const payload = JSON.stringify(exportTheme(activeTheme.value), null, 2)
  const link = document.createElement('a')
  link.href = URL.createObjectURL(new Blob([payload], { type: 'application/json' }))
  link.download = `${activeTheme.value.id}.theme.json`
  link.click()
  URL.revokeObjectURL(link.href)
}

function isZipArchive(file: File) {
  return file.name.toLowerCase().endsWith('.zip') || file.type.includes('zip')
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
      toast.success(`Imported ${result.imported.length} theme package${result.imported.length === 1 ? '' : 's'}`)
      if (result.skipped.length)
        toast.warning(`${result.skipped.length} file${result.skipped.length === 1 ? '' : 's'} skipped (${result.skipped[0]?.name})`)
    }
    else {
      const imported = importTheme(JSON.parse(await file.text()) as unknown)
      if (!imported)
        throw new Error('This file is not a compatible theme package.')
      toast.success(`${imported.name} imported`)
    }
  }
  catch (error) {
    toast.error(error instanceof Error ? error.message : 'Unable to import theme package.')
  }
  finally {
    input.value = ''
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
      <div class="space-y-1">
        <div class="flex items-center gap-2">
          <Palette class="size-5 text-primary" />
          <h2 class="text-2xl font-bold tracking-tight">Theme packages</h2>
        </div>
        <p class="text-sm text-muted-foreground">
          Compose the platform from shared visual tokens. Packages stay local to this browser until a server registry is introduced.
        </p>
      </div>
      <div class="flex items-center gap-2">
        <input ref="importInput" class="sr-only" type="file" accept="application/json,.json,.theme.json,application/zip,.zip,application/x-zip-compressed" @change="handleImport" />
        <Button variant="outline" size="icon" title="Import theme package" aria-label="Import theme package" @click="importInput?.click()">
          <Upload class="size-4" />
        </Button>
        <Button variant="outline" size="icon" title="Export active theme package" aria-label="Export active theme package" @click="exportActiveTheme">
          <Download class="size-4" />
        </Button>
        <Button @click="openCreateDialog">
          <Plus class="size-4" />
          New package
        </Button>
      </div>
    </div>

    <Panel class="grid gap-4 p-4 md:grid-cols-[1fr_auto] md:items-center">
      <div class="flex min-w-0 items-center gap-3">
        <div
          class="grid size-11 shrink-0 grid-cols-2 overflow-hidden border-2 border-foreground"
          aria-hidden="true"
        >
          <span v-for="color in [activeTheme.tokens['--background'], activeTheme.tokens['--card'], activeTheme.tokens['--primary'], activeTheme.tokens['--accent']]" :key="color" :style="{ backgroundColor: color }" />
        </div>
        <div class="min-w-0">
          <div class="flex flex-wrap items-center gap-2">
            <span class="font-semibold">{{ activeTheme.name }}</span>
            <Badge class="text-[10px]">Active package</Badge>
          </div>
          <p class="truncate text-sm text-muted-foreground">{{ activeTheme.description }}</p>
        </div>
      </div>
      <div class="flex items-center gap-2 text-sm text-muted-foreground">
        <span>{{ localThemeCount }} local</span>
        <span aria-hidden="true">/</span>
        <span>{{ themePackages.length }} available</span>
        <Button
          v-if="activeThemeId !== defaultThemeId"
          variant="ghost"
          size="icon"
          title="Restore NoCTF Pixel"
          aria-label="Restore NoCTF Pixel"
          @click="handleApply(defaultThemeId)"
        >
          <RotateCcw class="size-4" />
        </Button>
      </div>
    </Panel>

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <ThemePackageCard
        v-for="theme in themePackages"
        :key="theme.id"
        :theme="theme"
        :active="theme.id === activeThemeId"
        @apply="handleApply"
        @duplicate="handleDuplicate"
        @edit="handleEdit"
        @remove="requestRemove"
      />
    </div>

    <Card v-if="!themePackages.length" :decorated="false" class="border-dashed">
      <CardContent class="flex min-h-48 flex-col items-center justify-center gap-3 text-center">
        <Palette class="size-10 text-muted-foreground" />
        <div>
          <p class="font-semibold">No theme packages available</p>
          <p class="text-sm text-muted-foreground">Create a local package to begin.</p>
        </div>
      </CardContent>
    </Card>

    <ThemePackageEditorDialog
      v-model:open="editorOpen"
      :theme="editingTheme"
      @save="handleSave"
    />
    <ThemePackageDeleteDialog
      :open="Boolean(removingTheme)"
      :theme="removingTheme"
      @update:open="open => !open && (removingTheme = undefined)"
      @confirm="handleRemove"
    />
  </div>
</template>
