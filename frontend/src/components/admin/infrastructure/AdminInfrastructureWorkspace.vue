<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { Activity, Box, CheckCircle2, ServerCrash } from 'lucide-vue-next'
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

const { data, isLoading, isError, refetch } = useQuery({
  queryKey: ['admin-infrastructure'],
  queryFn: () => adminApi.infrastructure() as Promise<Infrastructure>,
  refetchInterval: 30_000,
})

const runnerInfo = computed(() => Object.entries(data.value?.runnerInfo ?? {}))
const kubernetes = computed(() => Object.entries(data.value?.kubernetes ?? {}).filter(([, value]) => value !== null && value !== undefined && value !== ''))
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-start justify-between gap-4">
      <div>
        <h2 class="text-2xl font-bold tracking-tight">Infrastructure</h2>
        <p class="mt-1 text-sm text-muted-foreground">Runner connectivity and cluster configuration.</p>
      </div>
      <Button variant="outline" :disabled="isLoading" @click="refetch()">Refresh</Button>
    </div>

    <DataState v-if="isLoading" loading />
    <DataState v-else-if="isError" error retry-label="Retry" @retry="refetch()" />

    <template v-else-if="data">
      <div class="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <Box class="size-5 text-primary" />
            <div><div class="text-xs text-muted-foreground">Runner</div><div class="font-semibold">{{ data.runnerProvider }}</div></div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <CheckCircle2 v-if="data.runnerReachable" class="size-5 text-primary" />
            <ServerCrash v-else class="size-5 text-destructive" />
            <div><div class="text-xs text-muted-foreground">Reachability</div><div class="font-semibold">{{ data.runnerReachable ? 'Connected' : 'Unavailable' }}</div></div>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="flex items-center gap-3 p-4">
            <Activity class="size-5 text-primary" />
            <div><div class="text-xs text-muted-foreground">Runner URL</div><div class="max-w-48 truncate font-mono text-xs">{{ data.runnerBaseUrl || 'Not configured' }}</div></div>
          </CardContent>
        </Card>
      </div>

      <div class="grid gap-6 xl:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Runner details</CardTitle></CardHeader>
          <CardContent class="space-y-2">
            <Panel v-for="[key, value] in runnerInfo" :key="key" class="flex items-center justify-between gap-4 p-3">
              <span class="text-sm text-muted-foreground">{{ key }}</span>
              <code class="max-w-[60%] truncate text-xs">{{ typeof value === 'object' ? JSON.stringify(value) : String(value) }}</code>
            </Panel>
            <p v-if="!runnerInfo.length" class="text-sm text-muted-foreground">No runner metadata is available.</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Cluster defaults</CardTitle></CardHeader>
          <CardContent class="space-y-2">
            <Panel v-for="[key, value] in kubernetes" :key="key" class="flex items-center justify-between gap-4 p-3">
              <span class="text-sm text-muted-foreground">{{ key }}</span>
              <Badge variant="outline" class="max-w-[60%] truncate font-mono">{{ String(value) }}</Badge>
            </Panel>
            <p v-if="!kubernetes.length" class="text-sm text-muted-foreground">No Kubernetes defaults are configured.</p>
          </CardContent>
        </Card>
      </div>
    </template>
  </div>
</template>
