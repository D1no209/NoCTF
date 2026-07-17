<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { builtInThemePackages, defaultThemeId } from '@/themes/presets'
import {
  isThemeTokenName,
  themeTokenGroups,
  type ThemePackage,
  type ThemePackageDraft,
} from '@/themes/theme-package'

const props = defineProps<{
  open: boolean
  theme?: ThemePackage
}>()

const emit = defineEmits<{
  'update:open': [open: boolean]
  save: [draft: ThemePackageDraft]
}>()

const defaultPreview = builtInThemePackages.find(theme => theme.id === defaultThemeId)!.preview
const form = reactive<ThemePackageDraft>({
  name: '',
  description: '',
  uiPackage: 'v1',
  preview: { ...defaultPreview },
  tokens: {},
})
const rawTokens = ref('{}')
const errorMessage = ref('')
const selectedTokenGroup = ref(themeTokenGroups[0]!.id)
const visibleTokenGroups = computed(() => themeTokenGroups)
const previewTokenMap = {
  background: ['--background'],
  surface: ['--card', '--popover'],
  primary: ['--primary', '--sidebar-primary', '--ring'],
  accent: ['--accent', '--secondary'],
} as const

function populateForm(theme?: ThemePackage) {
  form.name = theme?.name ?? ''
  form.description = theme?.description ?? ''
  form.uiPackage = theme?.uiPackage ?? 'v1'
  form.preview.background = theme?.preview.background ?? defaultPreview.background
  form.preview.surface = theme?.preview.surface ?? defaultPreview.surface
  form.preview.primary = theme?.preview.primary ?? defaultPreview.primary
  form.preview.accent = theme?.preview.accent ?? defaultPreview.accent
  form.tokens = { ...(theme?.tokens ?? {}) }
  syncPreviewFromTokens(form.tokens)
  rawTokens.value = JSON.stringify(form.tokens, null, 2)
  errorMessage.value = ''
  selectedTokenGroup.value = themeTokenGroups[0]!.id
}

function handleSubmit() {
  if (!form.name.trim())
    return

  try {
    form.tokens = parseTokenMap(rawTokens.value)
    syncPreviewFromTokens(form.tokens)
    errorMessage.value = ''
  }
  catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Token map is not valid JSON.'
    return
  }

  emit('save', {
    name: form.name,
    description: form.description,
    uiPackage: form.uiPackage,
    preview: { ...form.preview },
    tokens: { ...form.tokens },
  })
}

function updateToken(token: string, value: string) {
  try {
    form.tokens = parseTokenMap(rawTokens.value)
  }
  catch {
    // Preserve the last valid field state until the user corrects the raw JSON.
  }
  form.tokens[token] = value
  if (token === '--background')
    form.preview.background = value
  if (token === '--card')
    form.preview.surface = value
  if (token === '--primary')
    form.preview.primary = value
  if (token === '--accent')
    form.preview.accent = value
  rawTokens.value = JSON.stringify(form.tokens, null, 2)
}

function updatePreview(key: keyof typeof previewTokenMap, value: string) {
  form.preview[key] = value
  previewTokenMap[key].forEach(token => form.tokens[token] = value)
  rawTokens.value = JSON.stringify(form.tokens, null, 2)
}

function syncPreviewFromTokens(tokens: Record<string, string>) {
  form.preview.background = tokens['--background'] ?? form.preview.background
  form.preview.surface = tokens['--card'] ?? form.preview.surface
  form.preview.primary = tokens['--primary'] ?? form.preview.primary
  form.preview.accent = tokens['--accent'] ?? form.preview.accent
}

function parseTokenMap(value: string) {
  const parsed: unknown = JSON.parse(value || '{}')
  if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object')
    throw new Error('Token map must be an object.')
  return Object.entries(parsed).reduce<Record<string, string>>((result, [token, tokenValue]) => {
    if (!isThemeTokenName(token))
      throw new Error(`Unsupported token: ${token}`)
    if (typeof tokenValue !== 'string' || !tokenValue.length || tokenValue.length > 512)
      throw new Error(`Token values must be non-empty strings up to 512 characters: ${token}`)
    result[token] = tokenValue
    return result
  }, {})
}

watch(() => props.open, (open) => {
  if (open)
    populateForm(props.theme)
})
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="max-h-[88vh] max-w-3xl overflow-y-auto rounded-none border-2 bg-popover p-6">
      <DialogHeader>
        <DialogTitle>{{ props.theme ? 'Edit theme package' : 'Create theme package' }}</DialogTitle>
        <p class="text-sm text-muted-foreground">
          Theme packages are saved in this browser and applied immediately when selected.
        </p>
      </DialogHeader>

      <form class="grid gap-5" @submit.prevent="handleSubmit">
        <div class="grid gap-2">
          <Label for="theme-name">Name</Label>
          <Input id="theme-name" v-model="form.name" maxlength="48" placeholder="e.g. Incident Console" />
        </div>
        <div class="grid gap-2">
          <Label for="theme-description">Description</Label>
          <Textarea id="theme-description" v-model="form.description" rows="3" placeholder="A short note for other operators." />
        </div>
        <div class="grid gap-2">
          <Label for="theme-ui-package">UI package</Label>
          <Select v-model="form.uiPackage">
            <SelectTrigger id="theme-ui-package">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="v1">Pixel workstation (V1)</SelectItem>
              <SelectItem value="v2">Neon Command (V2)</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div class="grid grid-cols-2 gap-4 sm:grid-cols-4">
          <div class="grid gap-2">
            <Label for="theme-background">Canvas</Label>
            <Input id="theme-background" :model-value="form.preview.background" type="color" class="h-11 cursor-pointer p-1" @update:model-value="updatePreview('background', String($event))" />
          </div>
          <div class="grid gap-2">
            <Label for="theme-surface">Surface</Label>
            <Input id="theme-surface" :model-value="form.preview.surface" type="color" class="h-11 cursor-pointer p-1" @update:model-value="updatePreview('surface', String($event))" />
          </div>
          <div class="grid gap-2">
            <Label for="theme-primary">Primary</Label>
            <Input id="theme-primary" :model-value="form.preview.primary" type="color" class="h-11 cursor-pointer p-1" @update:model-value="updatePreview('primary', String($event))" />
          </div>
          <div class="grid gap-2">
            <Label for="theme-accent">Accent</Label>
            <Input id="theme-accent" :model-value="form.preview.accent" type="color" class="h-11 cursor-pointer p-1" @update:model-value="updatePreview('accent', String($event))" />
          </div>
        </div>

        <div class="space-y-5 border-t pt-5">
          <div>
            <h3 class="font-semibold">Global tokens</h3>
            <p class="mt-1 text-sm text-muted-foreground">These tokens drive primitives, shared layouts, AWD displays, and leaderboard visuals.</p>
          </div>
          <Tabs v-model="selectedTokenGroup" class="grid gap-3">
            <TabsList class="h-auto w-full justify-start overflow-x-auto rounded-none">
              <TabsTrigger v-for="group in visibleTokenGroups" :key="group.id" :value="group.id" class="shrink-0">
                {{ group.label }}
              </TabsTrigger>
            </TabsList>
            <TabsContent v-for="group in visibleTokenGroups" :key="group.id" :value="group.id" class="mt-0">
              <div class="grid gap-3 sm:grid-cols-2">
                <div v-for="field in group.fields" :key="field.token" class="grid gap-1.5">
                  <Label :for="`theme-token-${field.token}`">{{ field.label }}</Label>
                  <Input
                    :id="`theme-token-${field.token}`"
                    :model-value="form.tokens[field.token] ?? ''"
                    :placeholder="field.token"
                    @update:model-value="updateToken(field.token, String($event))"
                  />
                </div>
              </div>
            </TabsContent>
          </Tabs>
        </div>

        <div class="grid gap-2 border-t pt-5">
          <Label for="theme-token-json">Complete token map</Label>
          <Textarea
            id="theme-token-json"
            v-model="rawTokens"
            rows="10"
            class="font-mono text-xs"
            spellcheck="false"
          />
          <p v-if="errorMessage" class="text-sm text-destructive">{{ errorMessage }}</p>
          <p v-else class="text-sm text-muted-foreground">Advanced editing accepts every supported package token. Unsupported keys are rejected so imports cannot silently change unrelated UI.</p>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit">{{ props.theme ? 'Save changes' : 'Create package' }}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
