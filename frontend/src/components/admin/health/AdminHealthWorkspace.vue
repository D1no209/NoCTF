<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { adminApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { 
  HeartPulse, 
  Activity, 
  Database, 
  Zap, 
  Server, 
  AlertTriangle, 
  CheckCircle2, 
  RotateCw,
  Clock
} from 'lucide-vue-next'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { toast } from 'vue-sonner'

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

async function fetchHealth(silent = false) {
  if (!silent) loading.value = true
  try {
    health.value = await adminApi.health<HealthResponse>()
    lastUpdated.value = new Date()
  } catch {
    health.value = null
    toast.error(t('admin.health.checkFailed'))
  } finally {
    if (!silent) loading.value = false
  }
}

onMounted(() => {
  fetchHealth()
  intervalId = setInterval(() => fetchHealth(true), 15000)
})

onUnmounted(() => {
  if (intervalId) clearInterval(intervalId)
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'healthy') return 'default'
  if (s === 'degraded' || s === 'warning') return 'secondary'
  return 'destructive'
}

const overallHealthy = computed(() => health.value?.status.toLowerCase() === 'healthy')

function getServiceIcon(name: string) {
  const n = name.toLowerCase()
  if (n.includes('database') || n.includes('pg') || n.includes('sql')) return Database
  if (n.includes('redis') || n.includes('cache')) return Zap
  if (n.includes('rabbit') || n.includes('bus')) return Server
  return Activity
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.health.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.health.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-3">
        <div v-if="lastUpdated" class="flex items-center gap-1.5 rounded-full border bg-muted/50 px-3 py-1 text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
          <Clock class="size-3" />
          {{ t('common.lastUpdated') }} {{ lastUpdated.toLocaleTimeString() }}
        </div>
        <Button variant="outline" size="sm" :disabled="loading" @click="fetchHealth()">
          <RotateCw class="mr-2 size-4" :class="{ 'animate-spin': loading }" />
          {{ t('common.refresh') }}
        </Button>
      </div>
    </div>

    <!-- Overall Status Banner -->
    <div v-if="health" v-auto-animate>
      <div 
        class="relative flex flex-col items-center gap-6 overflow-hidden rounded-xl border p-6 transition-all duration-300 sm:flex-row"
        :class="overallHealthy ? 'bg-[var(--semantic-success-soft)] border-[var(--semantic-success-border)]' : 'bg-[var(--semantic-danger-soft)] border-[var(--semantic-danger-border)]'"
      >
        <div 
          class="flex size-16 items-center justify-center rounded-xl shadow-lg transition-transform hover:scale-105"
          :class="overallHealthy ? 'bg-[var(--semantic-success)] text-primary-foreground' : 'bg-destructive text-primary-foreground'"
        >
          <HeartPulse class="size-8" :class="{ 'animate-pulse': overallHealthy }" />
        </div>
        
        <div class="flex-1 text-center sm:text-left space-y-1">
          <h3 class="text-2xl font-black uppercase tracking-normal">
            {{ t('admin.health.overallStatus') }}: <span :class="overallHealthy ? 'text-[var(--semantic-success)]' : 'text-destructive'">{{ health.status }}</span>
          </h3>
          <p class="text-sm text-muted-foreground max-w-lg">
            {{ overallHealthy ? t('admin.health.operationalDescription') : t('admin.health.degradedDescription') }}
          </p>
        </div>

        <div class="hidden lg:flex items-center gap-2">
           <Badge variant="outline" class="border-[var(--semantic-success-border)] bg-[var(--semantic-success-soft)] text-[var(--semantic-success)]">
             <CheckCircle2 class="size-3 mr-1" /> {{ health.checks.length }} {{ t('admin.health.components') }}
           </Badge>
        </div>
      </div>
    </div>

    <!-- Skeleton Grid -->
    <div v-if="loading && !health" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
      <Skeleton v-for="i in 6" :key="i" class="h-32 rounded-xl" />
    </div>

    <!-- Component Grid -->
    <div v-if="health" v-auto-animate class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
      <Card 
        v-for="check in health.checks" 
        :key="check.name"
        class="group overflow-hidden transition-all duration-200 hover:-translate-y-0.5 hover:shadow-float"
      >
        <CardHeader class="pb-3 border-b bg-muted/20">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-3">
              <div class="bg-background p-2 rounded-lg border shadow-sm group-hover:text-primary transition-colors">
                <component :is="getServiceIcon(check.name)" class="size-4" />
              </div>
              <CardTitle class="text-sm font-bold uppercase tracking-wide truncate max-w-[120px]">
                {{ check.name }}
              </CardTitle>
            </div>
            <Badge :variant="statusVariant(check.status)" class="text-[9px] font-black tracking-widest uppercase px-1.5 h-4">
              {{ check.status }}
            </Badge>
          </div>
        </CardHeader>
        <CardContent class="pt-4">
          <div class="flex flex-col gap-2">
            <p v-if="check.description" class="text-xs text-muted-foreground leading-relaxed italic">
              {{ check.description }}
            </p>
            <div class="flex items-center gap-1.5 mt-2">
              <div class="size-1.5 rounded-full" :class="check.status === 'Healthy' ? 'bg-[var(--semantic-success)]' : 'bg-destructive'" />
              <span class="text-[10px] font-bold text-muted-foreground uppercase tracking-tighter">
                {{ check.status === 'Healthy' ? t('admin.health.operational') : t('admin.health.actionRequired') }}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card v-if="!loading && !health" class="flex flex-col items-center justify-center border-dashed py-20 text-center">
      <div class="size-16 rounded-full bg-destructive/10 flex items-center justify-center text-destructive mb-4">
        <AlertTriangle class="size-8" />
      </div>
      <h3 class="text-xl font-bold">{{ t('admin.health.unavailableTitle') }}</h3>
      <p class="text-sm text-muted-foreground mt-2 max-w-xs">{{ t('admin.health.unavailableDescription') }}</p>
      <Button variant="outline" class="mt-6" @click="fetchHealth()">{{ t('common.refresh') }}</Button>
    </Card>

    <div class="flex items-center gap-2 px-1 text-[10px] font-bold uppercase tracking-widest text-muted-foreground opacity-50">
      <RotateCw class="size-3 animate-spin" />
      {{ t('common.autoRefresh', { seconds: 15 }) }}
    </div>
  </div>
</template>
