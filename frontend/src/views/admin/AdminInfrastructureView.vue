<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { Activity, Cloud, Server } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

interface InfrastructureDto {
  runnerProvider: string
  runnerBaseUrl?: string | null
  runnerReachable: boolean
  runnerInfo?: Record<string, unknown> | null
  kubernetes?: Record<string, unknown>
}

const { t } = useI18n()
const { data, isLoading } = useQuery({
  queryKey: queryKeys.adminInfrastructure,
  queryFn: () => adminApi.infrastructure<InfrastructureDto>(),
})

const runnerInfo = computed(() => data.value?.runnerInfo ?? {})
const kubernetesInfo = computed(() => {
  const fromRunner = (runnerInfo.value?.kubernetes ?? {}) as Record<string, unknown>
  return { ...(data.value?.kubernetes ?? {}), ...fromRunner }
})

function display(value: unknown) {
  if (Array.isArray(value)) return value.length ? value.join(', ') : '-'
  if (value === undefined || value === null || value === '') return '-'
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}
</script>

<template>
  <div class="noctf-admin-page">
    <div>
      <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.infrastructure.title') }}</h2>
      <p class="text-sm text-muted-foreground">{{ t('admin.infrastructure.subtitle') }}</p>
    </div>

    <div v-if="isLoading" class="grid gap-4 md:grid-cols-3">
      <Skeleton v-for="index in 3" :key="index" class="h-32 rounded-lg" />
    </div>

    <div v-else class="grid gap-4 lg:grid-cols-3">
      <Card>
        <CardHeader class="flex flex-row items-center justify-between">
          <CardTitle class="text-base">{{ t('admin.infrastructure.runner') }}</CardTitle>
          <Server class="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent class="space-y-3 text-sm">
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">{{ t('admin.infrastructure.provider') }}</span>
            <Badge variant="outline">{{ data?.runnerProvider ?? '-' }}</Badge>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">{{ t('admin.infrastructure.status') }}</span>
            <Badge :variant="data?.runnerReachable ? 'default' : 'destructive'">
              {{ data?.runnerReachable ? t('admin.infrastructure.reachable') : t('admin.infrastructure.unreachable') }}
            </Badge>
          </div>
          <div class="break-all text-muted-foreground">{{ data?.runnerBaseUrl ?? '-' }}</div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader class="flex flex-row items-center justify-between">
          <CardTitle class="text-base">Kubernetes</CardTitle>
          <Cloud class="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent class="space-y-3 text-sm">
          <div v-for="key in ['connectionMode', 'defaultExposure', 'publicEntry', 'ingressBaseDomain', 'namespacePrefix']" :key="key" class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">{{ key }}</span>
            <span class="max-w-44 truncate font-medium">{{ display(kubernetesInfo[key]) }}</span>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader class="flex flex-row items-center justify-between">
          <CardTitle class="text-base">{{ t('admin.infrastructure.policy') }}</CardTitle>
          <Activity class="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent class="space-y-3 text-sm">
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">networkMode</span>
            <span class="font-medium">{{ display(kubernetesInfo.networkMode) }}</span>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">registryCount</span>
            <span class="font-medium">{{ display(kubernetesInfo.registryCount) }}</span>
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">imagePullSecrets</span>
            <span class="max-w-44 truncate font-medium">{{ display(kubernetesInfo.imagePullSecrets) }}</span>
          </div>
          <div class="break-all rounded-md bg-muted p-3 font-mono text-xs">
            {{ display(kubernetesInfo.quota) }}
          </div>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
