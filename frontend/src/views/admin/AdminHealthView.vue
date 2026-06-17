<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import PageHeader from '@/components/layout/PageHeader.vue'
import StatTile from '@/components/layout/StatTile.vue'
import DataState from '@/components/state/DataState.vue'

const { t } = useI18n()

interface HealthCheck {
  name: string
  status: string
  description?: string
}

interface HealthResponse {
  status: string
  checks: HealthCheck[]
}

const health = ref<HealthResponse | null>(null)
const loading = ref(false)
const lastUpdated = ref<Date | null>(null)
let intervalId: ReturnType<typeof setInterval> | null = null

async function fetchHealth() {
  loading.value = true
  try {
    health.value = await adminApi.health<HealthResponse>()
    lastUpdated.value = new Date()
  } catch {
    health.value = null
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchHealth()
  intervalId = setInterval(fetchHealth, 10000)
})

onUnmounted(() => {
  if (intervalId) clearInterval(intervalId)
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 'Healthy') return 'default'
  if (status === 'Degraded') return 'secondary'
  return 'destructive'
}

function statusIcon(status: string) {
  if (status === 'Healthy') return '🟢'
  if (status === 'Degraded') return '🟡'
  return '🔴'
}
</script>

<template>
  <div class="space-y-4 p-4 md:p-6">
    <PageHeader :title="t('admin.health.title')">
      <template #actions>
      <div class="flex items-center gap-3">
        <span v-if="lastUpdated" class="text-xs text-muted-foreground">
          {{ t('common.lastUpdated') }} {{ lastUpdated.toLocaleTimeString() }}
        </span>
        <Button variant="outline" size="sm" :disabled="loading" @click="fetchHealth()">
          {{ loading ? t('common.check') : t('common.refresh') }}
        </Button>
      </div>
      </template>
    </PageHeader>

    <!-- Overall status -->
    <StatTile
      v-if="health"
      :label="t('admin.health.overallStatus')"
      :value="health.status"
      :description="statusIcon(health.status)"
      :tone="health.status === 'Healthy' ? 'success' : health.status === 'Degraded' ? 'warning' : 'danger'"
    />

    <!-- Individual checks -->
    <div v-if="health" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
      <div
        v-for="check in health.checks"
        :key="check.name"
        class="p-4 rounded-lg border space-y-2"
        :class="check.status !== 'Healthy' ? 'border-destructive bg-destructive/5' : 'border-border'"
      >
        <div class="flex items-center justify-between">
          <span class="font-medium capitalize">{{ check.name }}</span>
          <span class="text-lg">{{ statusIcon(check.status) }}</span>
        </div>
        <Badge :variant="statusVariant(check.status)">{{ check.status }}</Badge>
        <p v-if="check.description" class="text-xs text-muted-foreground">{{ check.description }}</p>
      </div>
    </div>

    <DataState
      v-else
      :loading="loading"
      :error="!loading"
      :loading-title="t('admin.health.checking')"
      :error-title="t('admin.health.unableToFetch')"
      :retry-label="t('common.refresh')"
      @retry="fetchHealth()"
    />

    <p class="text-xs text-muted-foreground">{{ t('admin.health.autoRefresh') }}</p>
  </div>
</template>
