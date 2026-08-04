<script setup lang="ts">
import type { PlatformConfiguration } from '@/api/noctf'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ImageUp, Loader2, RefreshCw, Save, Settings2 } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { platformAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import BrandLogo from '@/components/BrandLogo.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'

const maximumLogoBytes = 5 * 1024 * 1024
const supportedLogoTypes = new Set(['image/jpeg', 'image/png', 'image/webp'])

const { t } = useI18n()
const queryClient = useQueryClient()
const logoInput = ref<HTMLInputElement | null>(null)

const form = reactive({
  name: '',
  description: '',
  logoUrl: '',
  revision: 0,
  updatedAt: '',
})

const {
  data: configuration,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.adminPlatformConfiguration,
  queryFn: platformAdminApi.platformConfiguration,
})

function applyConfiguration(value: PlatformConfiguration) {
  form.name = value.name ?? ''
  form.description = value.description ?? ''
  form.logoUrl = value.logoUrl ?? ''
  form.revision = value.revision ?? 0
  form.updatedAt = value.updatedAt ?? ''
}

watch(configuration, (value) => {
  if (value)
    applyConfiguration(value)
}, { immediate: true })

async function refreshBranding(configuration: PlatformConfiguration) {
  applyConfiguration(configuration)
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.adminPlatformConfiguration }),
    queryClient.invalidateQueries({ queryKey: queryKeys.platformConfiguration }),
  ])
}

const saveMutation = useMutation({
  mutationFn: () => platformAdminApi.updatePlatformConfiguration({
    name: form.name.trim(),
    description: form.description.trim() || null,
    expectedRevision: form.revision,
  }),
  onSuccess: async (value) => {
    await refreshBranding(value)
    toast.success(t('admin.settings.basic.saved'))
  },
  onError: async () => {
    toast.error(t('admin.settings.basic.saveFailed'))
    await refetch()
  },
})

const logoMutation = useMutation({
  mutationFn: (file: File) => platformAdminApi.uploadPlatformLogo(file, form.revision),
  onSuccess: async (value) => {
    await refreshBranding(value)
    toast.success(t('admin.settings.basic.logoUploaded'))
  },
  onError: async () => {
    toast.error(t('admin.settings.basic.logoUploadFailed'))
    await refetch()
  },
})

const canSave = computed(() =>
  form.name.trim().length > 0
  && form.name.trim().length <= 100
  && form.description.trim().length <= 500)

const lastUpdated = computed(() => {
  if (!form.updatedAt)
    return t('admin.settings.basic.initialConfiguration')

  const date = new Date(form.updatedAt)
  return Number.isNaN(date.getTime())
    ? form.updatedAt
    : new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(date)
})

function selectLogo() {
  logoInput.value?.click()
}

function onLogoSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file)
    return
  if (!supportedLogoTypes.has(file.type) || file.size < 1 || file.size > maximumLogoBytes) {
    toast.error(t('admin.settings.basic.logoInvalid'))
    return
  }

  logoMutation.mutate(file)
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
      <div class="space-y-1">
        <div class="flex items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">
            {{ t('admin.settings.basic.title') }}
          </h2>
          <Badge variant="destructive" class="rounded-none">
            {{ t('nav.admin') }}
          </Badge>
        </div>
        <p class="max-w-2xl text-sm text-muted-foreground">
          {{ t('admin.settings.basic.subtitle') }}
        </p>
      </div>
      <Button variant="outline" :disabled="isLoading" @click="refetch()">
        <RefreshCw class="size-4" :class="{ 'animate-spin': isLoading }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div v-if="isLoading" class="grid gap-4 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <Skeleton class="h-72 rounded-none" />
      <Skeleton class="h-72 rounded-none" />
    </div>

    <Alert v-else-if="isError" variant="destructive" class="rounded-none border-2">
      <AlertTitle>{{ t('state.failedToLoad') }}</AlertTitle>
      <AlertDescription class="mt-3">
        <Button variant="outline" size="sm" @click="refetch()">
          {{ t('common.retry') }}
        </Button>
      </AlertDescription>
    </Alert>

    <div v-else class="grid gap-5 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle class="flex items-center gap-2">
            <ImageUp class="size-5" />
            {{ t('admin.settings.basic.logo') }}
          </CardTitle>
        </CardHeader>
        <CardContent class="space-y-5 pt-6">
          <div class="flex h-28 items-center justify-center border-2 border-dashed bg-muted/30 p-5">
            <img
              v-if="form.logoUrl"
              :src="form.logoUrl"
              :alt="form.name"
              class="max-h-full max-w-full object-contain"
            >
            <BrandLogo v-else class="h-14 max-w-full" />
          </div>
          <input
            ref="logoInput"
            type="file"
            class="hidden"
            accept="image/png,image/jpeg,image/webp"
            @change="onLogoSelected"
          >
          <Button
            variant="outline"
            class="w-full"
            :disabled="logoMutation.isPending.value"
            @click="selectLogo"
          >
            <Loader2 v-if="logoMutation.isPending.value" class="size-4 animate-spin" />
            <ImageUp v-else class="size-4" />
            {{ t('admin.settings.basic.replaceLogo') }}
          </Button>
          <p class="text-xs leading-5 text-muted-foreground">
            {{ t('admin.settings.basic.logoHint') }}
          </p>
        </CardContent>
      </Card>

      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle class="flex items-center gap-2">
            <Settings2 class="size-5" />
            {{ t('admin.settings.basic.identity') }}
          </CardTitle>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.settings.basic.identityDescription') }}
          </p>
        </CardHeader>
        <CardContent class="space-y-5 pt-6">
          <div class="space-y-2">
            <Label for="platform-name">{{ t('admin.settings.basic.name') }}</Label>
            <Input id="platform-name" v-model="form.name" maxlength="100" />
            <p class="text-right text-xs tabular-nums text-muted-foreground">
              {{ form.name.length }}/100
            </p>
          </div>
          <div class="space-y-2">
            <Label for="platform-description">{{ t('admin.settings.basic.description') }}</Label>
            <Textarea
              id="platform-description"
              v-model="form.description"
              maxlength="500"
              rows="5"
              :placeholder="t('admin.settings.basic.descriptionPlaceholder')"
            />
            <p class="text-right text-xs tabular-nums text-muted-foreground">
              {{ form.description.length }}/500
            </p>
          </div>
          <div class="flex flex-col gap-3 border-t-2 pt-5 sm:flex-row sm:items-center sm:justify-between">
            <p class="text-xs text-muted-foreground">
              {{ t('admin.settings.basic.lastUpdated', { value: lastUpdated }) }}
            </p>
            <Button
              :disabled="saveMutation.isPending.value || !canSave"
              @click="saveMutation.mutate()"
            >
              <Loader2 v-if="saveMutation.isPending.value" class="size-4 animate-spin" />
              <Save v-else class="size-4" />
              {{ t('common.save') }}
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
