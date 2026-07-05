<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { Activity, Cloud, Server } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Badge } from '@/components/ui/badge'
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

    <div v-if="isLoading" class="noctf-workbench p-4">
      <div class="grid gap-4 md:grid-cols-3">
        <Skeleton v-for="index in 3" :key="index" class="h-28 rounded-md" />
      </div>
    </div>

    <div v-else class="noctf-workbench">
      <div class="grid border-b border-border/80 md:grid-cols-[1.1fr_1fr_1fr]">
        <div class="flex min-w-0 items-start gap-3 p-4">
          <div
            class="flex size-9 items-center justify-center rounded-md bg-primary/10 text-primary"
          >
            <Server class="size-4" />
          </div>
          <div class="min-w-0 space-y-2">
            <div class="flex flex-wrap items-center gap-2">
              <h3 class="text-sm font-semibold">{{ t('admin.infrastructure.runner') }}</h3>
              <Badge variant="outline">{{ data?.runnerProvider ?? '-' }}</Badge>
              <Badge :variant="data?.runnerReachable ? 'success' : 'destructive'">
                {{
                  data?.runnerReachable
                    ? t('admin.infrastructure.reachable')
                    : t('admin.infrastructure.unreachable')
                }}
              </Badge>
            </div>
            <p class="break-all font-mono text-xs text-muted-foreground">
              {{ data?.runnerBaseUrl ?? '-' }}
            </p>
          </div>
        </div>

        <div
          class="flex min-w-0 items-start gap-3 border-t border-border/80 p-4 md:border-l md:border-t-0"
        >
          <div
            class="flex size-9 items-center justify-center rounded-md bg-muted text-muted-foreground"
          >
            <Cloud class="size-4" />
          </div>
          <div class="min-w-0">
            <h3 class="text-sm font-semibold">Kubernetes</h3>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ display(kubernetesInfo.connectionMode) }} /
              {{ display(kubernetesInfo.defaultExposure) }}
            </p>
          </div>
        </div>

        <div
          class="flex min-w-0 items-start gap-3 border-t border-border/80 p-4 md:border-l md:border-t-0"
        >
          <div
            class="flex size-9 items-center justify-center rounded-md bg-muted text-muted-foreground"
          >
            <Activity class="size-4" />
          </div>
          <div class="min-w-0">
            <h3 class="text-sm font-semibold">{{ t('admin.infrastructure.policy') }}</h3>
            <p class="mt-1 text-xs text-muted-foreground">
              network: {{ display(kubernetesInfo.networkMode) }}
            </p>
          </div>
        </div>
      </div>

      <div class="grid gap-0 md:grid-cols-2">
        <div class="divide-y divide-border/70 p-4">
          <div
            v-for="key in ['publicEntry', 'ingressBaseDomain', 'namespacePrefix']"
            :key="key"
            class="grid gap-1 py-2 text-sm sm:grid-cols-[11rem_minmax(0,1fr)] sm:gap-3"
          >
            <span class="text-muted-foreground">{{ key }}</span>
            <span class="min-w-0 truncate font-medium">{{ display(kubernetesInfo[key]) }}</span>
          </div>
        </div>
        <div
          class="divide-y divide-border/70 border-t border-border/80 p-4 md:border-l md:border-t-0"
        >
          <div
            v-for="key in ['registryCount', 'imagePullSecrets']"
            :key="key"
            class="grid gap-1 py-2 text-sm sm:grid-cols-[11rem_minmax(0,1fr)] sm:gap-3"
          >
            <span class="text-muted-foreground">{{ key }}</span>
            <span class="min-w-0 truncate font-medium">{{ display(kubernetesInfo[key]) }}</span>
          </div>
          <div class="grid gap-2 py-2 text-sm">
            <span class="text-muted-foreground">quota</span>
            <code
              class="block max-h-28 overflow-auto rounded-md bg-muted/50 p-3 font-mono text-xs text-foreground/80"
            >
              {{ display(kubernetesInfo.quota) }}
            </code>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
