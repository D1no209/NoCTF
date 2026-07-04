<script setup lang="ts">
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useQuery } from '@tanstack/vue-query'
import { CheckCircle2, Gamepad2, HardDrive, Plug, Puzzle, ShieldCheck } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()

interface PluginDto {
  name: string
  type: string
  version: string
}

const { data: plugins, isLoading } = useQuery({
  queryKey: queryKeys.adminPlugins,
  queryFn: () => adminApi.plugins<PluginDto[]>(),
})

function typeVariant(type: string): 'default' | 'secondary' | 'outline' | 'destructive' {
  const t = type.toLowerCase()
  if (t.includes('gamemode')) return 'default'
  if (t.includes('challenge')) return 'secondary'
  if (t.includes('container')) return 'outline'
  return 'outline'
}

function getPluginIcon(type: string) {
  const t = type.toLowerCase()
  if (t.includes('gamemode')) return Gamepad2
  if (t.includes('challenge')) return Puzzle
  if (t.includes('container')) return HardDrive
  return Plug
}
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">
          {{ t('admin.plugins.title') }}
        </h2>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.plugins.subtitle') }}
        </p>
      </div>
    </div>

    <div v-if="plugins" class="noctf-status-strip grid-cols-1 sm:grid-cols-2">
      <div class="noctf-status-item border-b sm:border-b-0 sm:border-r">
        <span class="noctf-label">{{ t('admin.plugins.totalPlugins') }}</span>
        <Badge variant="secondary" class="font-mono">
          {{ plugins.length }}
        </Badge>
      </div>
      <div class="noctf-status-item">
        <ShieldCheck class="size-4 text-emerald-600" />
        <span class="noctf-label">{{ t('admin.plugins.coreEngine') }}</span>
        <span class="text-xs font-mono opacity-60">v1.0.0-stable</span>
      </div>
    </div>

    <div v-if="isLoading" class="noctf-workbench divide-y divide-border/80">
      <Skeleton v-for="i in 8" :key="i" class="h-16 rounded-none" />
    </div>

    <div v-else v-auto-animate class="noctf-workbench divide-y divide-border/80">
      <div
        v-for="plugin in plugins"
        :key="plugin.name"
        class="grid gap-3 p-4 transition-colors hover:bg-muted/35 md:grid-cols-[minmax(0,1fr)_auto]"
      >
        <div class="flex min-w-0 items-center gap-3">
          <div
            class="flex size-9 shrink-0 items-center justify-center rounded-md border border-primary/15 bg-primary/5 text-primary"
          >
            <component :is="getPluginIcon(plugin.type)" class="size-5" />
          </div>
          <div class="min-w-0">
            <div class="truncate text-sm font-semibold">
              {{ plugin.name }}
            </div>
            <div
              class="mt-1 flex items-center gap-2 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground"
            >
              {{ plugin.type }}
            </div>
          </div>
        </div>
        <div class="flex flex-wrap items-center gap-2 md:justify-end">
          <Badge variant="outline" class="font-mono text-[10px]"> v{{ plugin.version }} </Badge>
          <Badge :variant="typeVariant(plugin.type)" class="uppercase">
            {{ t('admin.plugins.active') }}
          </Badge>
          <div class="flex items-center gap-1 text-xs text-muted-foreground">
            <CheckCircle2 class="size-3 text-emerald-600" />
            {{ t('admin.plugins.verified') }}
          </div>
        </div>
      </div>
    </div>

    <div v-if="!isLoading && plugins?.length === 0" class="noctf-state-box py-20">
      <Plug class="size-12 text-muted-foreground mb-4 opacity-20" />
      <h3 class="text-lg font-medium">
        {{ t('admin.plugins.empty') }}
      </h3>
      <p class="text-sm text-muted-foreground mt-1">
        {{ t('admin.plugins.emptyDescription') }}
      </p>
    </div>
  </div>
</template>
