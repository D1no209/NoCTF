<script setup lang="ts">
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useQuery } from '@tanstack/vue-query'
import {
  CheckCircle2,
  Gamepad2,
  HardDrive,
  Plug,
  Puzzle,
  ShieldCheck,
} from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
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
  if (t.includes('gamemode'))
    return 'default'
  if (t.includes('challenge'))
    return 'secondary'
  if (t.includes('container'))
    return 'outline'
  return 'outline'
}

function getPluginIcon(type: string) {
  const t = type.toLowerCase()
  if (t.includes('gamemode'))
    return Gamepad2
  if (t.includes('challenge'))
    return Puzzle
  if (t.includes('container'))
    return HardDrive
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

    <!-- Stats Summary -->
    <div v-if="plugins" class="flex flex-wrap items-center gap-6 rounded-xl border border-slate-900/10 bg-white p-4 shadow-[0_8px_28px_rgb(15_23_42/0.035)]">
      <div class="flex items-center gap-2">
        <span class="noctf-label">{{ t('admin.plugins.totalPlugins') }}</span>
        <Badge variant="secondary" class="font-mono">
          {{ plugins.length }}
        </Badge>
      </div>
      <div class="h-4 w-px bg-border" />
      <div class="flex items-center gap-2">
        <ShieldCheck class="size-4 text-emerald-500" />
        <span class="noctf-label">{{ t('admin.plugins.coreEngine') }}</span>
        <span class="text-xs font-mono opacity-60">v1.0.0-stable</span>
      </div>
    </div>

    <!-- Skeleton Grid -->
    <div v-if="isLoading" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
      <Skeleton v-for="i in 8" :key="i" class="h-40 rounded-xl" />
    </div>

    <!-- Plugin Grid -->
    <div v-else v-auto-animate class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
      <Card
        v-for="plugin in plugins"
        :key="plugin.name"
        class="group flex flex-col overflow-hidden transition-all duration-200 hover:border-primary/20 hover:shadow-[0_14px_38px_rgb(15_23_42/0.08)]"
      >
        <CardHeader class="pb-3 space-y-4">
          <div class="flex items-center justify-between">
            <div class="bg-primary/5 p-2 rounded-lg border border-primary/10 text-primary group-hover:bg-primary group-hover:text-primary-foreground transition-all duration-300">
              <component :is="getPluginIcon(plugin.type)" class="size-5" />
            </div>
            <Badge variant="outline" class="font-mono text-[10px] opacity-70">
              v{{ plugin.version }}
            </Badge>
          </div>
          <div>
            <CardTitle class="text-base font-bold truncate">
              {{ plugin.name }}
            </CardTitle>
            <CardDescription class="text-[10px] font-black uppercase tracking-widest mt-1.5 flex items-center gap-1.5">
              <div class="size-1 rounded-full bg-primary" />
              {{ plugin.type }}
            </CardDescription>
          </div>
        </CardHeader>

        <CardContent class="mt-auto pt-0">
          <div class="flex items-center justify-between border-t pt-4 mt-2">
            <Badge :variant="typeVariant(plugin.type)" class="text-[9px] px-1.5 h-4 uppercase tracking-tighter">
              {{ t('admin.plugins.active') }}
            </Badge>
            <div class="flex items-center gap-1 text-[10px] text-muted-foreground font-medium">
              <CheckCircle2 class="size-3 text-emerald-500" />
              {{ t('admin.plugins.verified') }}
            </div>
          </div>
        </CardContent>
      </Card>
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
