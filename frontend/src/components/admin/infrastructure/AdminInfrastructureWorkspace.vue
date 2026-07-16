<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { Activity, Box, CheckCircle2, ServerCrash } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import DataState from '@/components/state/DataState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Panel } from '@/components/ui/panel'

interface Infrastructure {
  runnerProvider: string
  runnerBaseUrl?: string | null
  runnerReachable: boolean
  runnerInfo?: Record<string, unknown> | null
  kubernetes?: Record<string, unknown> | null
}

const { t, te } = useI18n()

const { data, isLoading, isError, refetch } = useQuery({
  queryKey: ['admin-infrastructure'],
  queryFn: () => adminApi.infrastructure() as Promise<Infrastructure>,
  refetchInterval: 30_000,
})

const runnerInfo = computed(() => Object.entries(data.value?.runnerInfo ?? {}))
const kubernetes = computed(() => Object.entries(data.value?.kubernetes ?? {}).filter(([, value]) => value !== null && value !== undefined && value !== ''))

function fieldLabel(section: 'runnerFields' | 'kubernetesFields', key: string) {
  const translationKey = `admin.infrastructure.${section}.${key}`
  return te(translationKey) ? t(translationKey) : key
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-start justify-between gap-4">
      <div>
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.infrastructure.title') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ t('admin.infrastructure.subtitle') }}</p>
      </div>
      <Button variant="outline" :disabled="isLoading" @click="refetch()">{{ t('common.refresh') }}</Button>
    </div>

    <DataState v-if="isLoading" loading />
    <DataState v-else-if="isError" error :retry-label="t('admin.infrastructure.retry')" @retry="refetch()" />

    <template v-else-if="data">
      <div class="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <Box class="size-5 text-primary" />
            <div><div class="text-xs text-muted-foreground">{{ t('admin.infrastructure.runner') }}</div><div class="font-semibold">{{ data.runnerProvider }}</div></div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <CheckCircle2 v-if="data.runnerReachable" class="size-5 text-primary" />
            <ServerCrash v-else class="size-5 text-destructive" />
            <div><div class="text-xs text-muted-foreground">{{ t('admin.infrastructure.reachability') }}</div><div class="font-semibold">{{ data.runnerReachable ? t('admin.infrastructure.connected') : t('admin.infrastructure.unavailable') }}</div></div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <Activity class="size-5 text-primary" />
            <div><div class="text-xs text-muted-foreground">{{ t('admin.infrastructure.runnerUrl') }}</div><div class="max-w-48 truncate font-mono text-xs">{{ data.runnerBaseUrl || t('admin.infrastructure.notConfigured') }}</div></div>
          </CardContent>
        </Card>
      </div>

      <div class="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>{{ t('admin.infrastructure.runnerDetails') }}</CardTitle></CardHeader>
          <CardContent class="space-y-2">
            <Panel v-for="[key, value] in runnerInfo" :key="key" class="flex items-center justify-between gap-4 p-3">
              <span class="text-sm text-muted-foreground">{{ fieldLabel('runnerFields', key) }}</span>
              <code class="max-w-[60%] truncate text-xs">{{ typeof value === 'object' ? JSON.stringify(value) : String(value) }}</code>
            </Panel>
            <p v-if="!runnerInfo.length" class="text-sm text-muted-foreground">{{ t('admin.infrastructure.noRunnerMetadata') }}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>{{ t('admin.infrastructure.clusterDefaults') }}</CardTitle></CardHeader>
          <CardContent class="space-y-2">
            <Panel v-for="[key, value] in kubernetes" :key="key" class="flex items-center justify-between gap-4 p-3">
              <span class="text-sm text-muted-foreground">{{ fieldLabel('kubernetesFields', key) }}</span>
              <Badge variant="outline" class="max-w-[60%] truncate font-mono">{{ String(value) }}</Badge>
            </Panel>
            <p v-if="!kubernetes.length" class="text-sm text-muted-foreground">{{ t('admin.infrastructure.noKubernetesDefaults') }}</p>
          </CardContent>
        </Card>
      </div>
    </template>
  </div>
</template>
