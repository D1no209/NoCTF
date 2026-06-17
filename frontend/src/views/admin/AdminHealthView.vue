<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { client } from '@/api/generated/client.gen'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'

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
    const res = await client.get<{ 200: HealthResponse }, unknown, false>({ url: '/api/health' })
    health.value = res.data ?? null
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
  <div class="p-6 space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold">{{ t('admin.health.title') }}</h1>
      <div class="flex items-center gap-3">
        <span v-if="lastUpdated" class="text-xs text-muted-foreground">
          {{ t('common.lastUpdated') }} {{ lastUpdated.toLocaleTimeString() }}
        </span>
        <Button variant="outline" size="sm" :disabled="loading" @click="fetchHealth()">
          {{ loading ? t('common.check') : t('common.refresh') }}
        </Button>
      </div>
    </div>

    <!-- Overall status -->
    <div v-if="health" class="flex items-center gap-3 p-4 rounded-lg border">
      <span class="text-2xl">{{ statusIcon(health.status) }}</span>
      <div>
        <p class="font-semibold">{{ t('admin.health.overallStatus') }}</p>
        <Badge :variant="statusVariant(health.status)">{{ health.status }}</Badge>
      </div>
    </div>

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

    <div v-else-if="!loading" class="text-center text-muted-foreground py-8">
      {{ t('admin.health.unableToFetch') }}
    </div>

    <div v-if="loading && !health" class="text-center text-muted-foreground py-8">
      {{ t('admin.health.checking') }}
    </div>

    <p class="text-xs text-muted-foreground">{{ t('admin.health.autoRefresh') }}</p>
  </div>
</template>
