<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/ui-v1/components/ui/card'
import { Skeleton } from '@/ui-v1/components/ui/skeleton'
import {
  Bot,
  Plug,
  Gamepad2,
  Swords,
  CheckCircle2,
  ShieldCheck
} from 'lucide-vue-next'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { useAdminPluginsPage } from '@/features/admin/useAdminPluginsPage'

const { t } = useI18n()

const { plugins, isLoading } = useAdminPluginsPage()

const coreEngine = computed(() => plugins.value?.find(plugin => plugin.type === 'CoreEngine'))
const pluginModules = computed(() => plugins.value?.filter(plugin => plugin.type === 'PluginModule') ?? [])

function getPluginIcon(name: string) {
  const normalized = name.toLowerCase()
  if (normalized.includes('qqbot'))
    return Bot
  if (normalized.includes('awd') || normalized.includes('penetration'))
    return Swords
  if (normalized.includes('ctf') || normalized.includes('koh'))
    return Gamepad2
  return Plug
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.plugins.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.plugins.subtitle') }}</p>
      </div>
    </div>

    <!-- Stats Summary -->
    <Card v-if="plugins" class="p-0">
      <CardContent class="flex flex-wrap items-center gap-6 p-4">
        <div class="flex items-center gap-2">
          <span>{{ t('admin.plugins.totalPlugins') }}</span>
          <Badge variant="secondary" class="font-mono">{{ pluginModules.length }}</Badge>
        </div>
        <div class="h-4 w-px bg-border" />
        <div class="flex items-center gap-2">
          <ShieldCheck class="size-4 text-[var(--semantic-success)]" />
          <span>{{ t('admin.plugins.coreEngine') }}</span>
          <span class="font-mono text-xs opacity-60">v{{ coreEngine?.version ?? '—' }}</span>
        </div>
      </CardContent>
    </Card>

    <!-- Skeleton Grid -->
    <div v-if="isLoading" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
      <Skeleton v-for="i in 8" :key="i" class="h-40" />
    </div>

    <!-- Plugin Grid -->
    <div v-else v-auto-animate class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
      <Card
        v-for="plugin in pluginModules"
        :key="plugin.name"
        class="group overflow-hidden transition-colors hover:bg-accent"
      >
        <CardHeader class="pb-3 space-y-4">
          <div class="flex items-center justify-between">
            <div class="bg-primary/5 p-2 rounded-lg border border-primary/10 text-primary group-hover:bg-primary group-hover:text-primary-foreground transition-all duration-300">
              <component :is="getPluginIcon(plugin.name)" class="size-5" />
            </div>
            <Badge variant="outline" class="font-mono text-[10px] opacity-70">
              v{{ plugin.version }}
            </Badge>
          </div>
          <div>
            <CardTitle class="text-base font-bold truncate">{{ plugin.name }}</CardTitle>
            <CardDescription class="text-[10px] font-black uppercase tracking-widest mt-1.5 flex items-center gap-1.5">
              <div class="size-1 rounded-full bg-primary" />
              {{ t('admin.plugins.pluginModule') }}
            </CardDescription>
          </div>
        </CardHeader>
        
        <CardContent class="mt-auto pt-0">
          <div class="flex items-center justify-between border-t pt-4 mt-2">
            <Badge variant="outline" class="h-4 px-1.5 text-[9px] uppercase tracking-tighter">
              {{ t('admin.plugins.active') }}
            </Badge>
            <div class="flex items-center gap-1 text-[10px] text-muted-foreground font-medium">
              <CheckCircle2 class="size-3 text-[var(--semantic-success)]" />
              {{ t('admin.plugins.verified') }}
            </div>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card v-if="!isLoading && pluginModules.length === 0" class="flex flex-col items-center justify-center border-dashed py-20 text-center">
      <Plug class="size-12 text-muted-foreground mb-4 opacity-20" />
      <h3 class="text-lg font-medium">{{ t('admin.plugins.empty') }}</h3>
      <p class="text-sm text-muted-foreground mt-1">{{ t('admin.plugins.emptyDescription') }}</p>
    </Card>
  </div>
</template>
