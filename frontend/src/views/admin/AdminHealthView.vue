<script setup lang="ts">
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  Clock,
  Database,
  HeartPulse,
  RotateCw,
  Server,
} from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'

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
  intervalId = setInterval(fetchHealth, 15000, true)
})

onUnmounted(() => {
  if (intervalId) clearInterval(intervalId)
})

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  const s = status.toLowerCase()
  if (s === 'healthy') return 'outline'
  if (s === 'degraded' || s === 'warning') return 'secondary'
  return 'destructive'
}

function statusBadgeClass(status: string) {
  const s = status.toLowerCase()
  if (s === 'healthy') {
    return 'border-emerald-600/20 bg-emerald-600 text-white hover:bg-emerald-600'
  }
  if (s === 'degraded' || s === 'warning') {
    return 'border-amber-500/25 bg-amber-500/10 text-amber-700'
  }
  return undefined
}

const overallHealthy = computed(() => health.value?.status.toLowerCase() === 'healthy')

function normalizedServiceName(name: string) {
  return name.toLowerCase()
}

function isRedisService(name: string) {
  const n = normalizedServiceName(name)
  return n.includes('redis') || n.includes('cache')
}

function isDockerService(name: string) {
  const n = normalizedServiceName(name)
  return n.includes('docker') || n.includes('container')
}

function serviceIconShellClass(name: string) {
  if (isRedisService(name)) return 'border-red-500/20 bg-red-500/10 text-red-600'
  if (isDockerService(name)) return 'border-sky-500/20 bg-sky-500/10 text-sky-600'
  return 'border-border bg-background/70 text-muted-foreground'
}

function getServiceIcon(name: string) {
  const n = name.toLowerCase()
  if (n.includes('database') || n.includes('pg') || n.includes('sql')) return Database
  if (n.includes('rabbit') || n.includes('bus')) return Server
  return Activity
}
</script>

<template>
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">
          {{ t('admin.health.title') }}
        </h2>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.health.subtitle') }}
        </p>
      </div>
      <div class="flex items-center gap-3">
        <div
          v-if="lastUpdated"
          class="flex items-center gap-1.5 rounded-full border bg-muted/50 px-3 py-1 text-[10px] font-bold uppercase tracking-wider text-muted-foreground"
        >
          <Clock class="size-3" />
          {{ t('common.lastUpdated') }} {{ lastUpdated.toLocaleTimeString() }}
        </div>
        <Button variant="outline" size="sm" :disabled="loading" @click="fetchHealth()">
          <RotateCw class="mr-2 size-4" :class="{ 'animate-spin': loading }" />
          {{ t('common.refresh') }}
        </Button>
      </div>
    </div>

    <div v-if="health" v-auto-animate>
      <div
        class="flex flex-col gap-4 rounded-lg border p-4 sm:flex-row sm:items-center"
        :class="
          overallHealthy
            ? 'border-emerald-500/25 bg-emerald-500/5'
            : 'border-destructive/25 bg-destructive/5'
        "
      >
        <div
          class="flex size-11 shrink-0 items-center justify-center rounded-md"
          :class="
            overallHealthy
              ? 'bg-emerald-500/10 text-emerald-700'
              : 'bg-destructive/10 text-destructive'
          "
        >
          <HeartPulse class="size-5" />
        </div>

        <div class="min-w-0 flex-1 space-y-1">
          <h3 class="text-lg font-semibold">
            {{ t('admin.health.overallStatus') }}:
            <span :class="overallHealthy ? 'text-emerald-700' : 'text-destructive'">{{
              health.status
            }}</span>
          </h3>
          <p class="text-sm text-muted-foreground max-w-lg">
            {{
              overallHealthy
                ? t('admin.health.operationalDescription')
                : t('admin.health.degradedDescription')
            }}
          </p>
        </div>

        <div class="flex items-center gap-2">
          <Badge variant="outline" class="bg-background/50">
            <CheckCircle2 class="size-3 mr-1" /> {{ health.checks.length }}
            {{ t('admin.health.components') }}
          </Badge>
        </div>
      </div>
    </div>

    <div v-if="loading && !health" class="noctf-workbench divide-y divide-border/80">
      <Skeleton v-for="i in 6" :key="i" class="h-16 rounded-none" />
    </div>

    <div v-if="health" v-auto-animate class="noctf-workbench divide-y divide-border/80">
      <div
        v-for="check in health.checks"
        :key="check.name"
        class="grid gap-3 p-4 transition-colors hover:bg-muted/35 md:grid-cols-[minmax(0,1fr)_auto]"
      >
        <div class="flex min-w-0 items-start gap-3">
          <div
            class="flex size-9 shrink-0 items-center justify-center rounded-md border"
            :class="serviceIconShellClass(check.name)"
          >
            <svg
              v-if="isRedisService(check.name)"
              class="size-5"
              viewBox="0 0 24 24"
              fill="none"
              aria-hidden="true"
            >
              <path d="M4 7.4 12 3l8 4.4-8 4.4L4 7.4Z" fill="currentColor" opacity=".95" />
              <path
                d="m4 11.1 8 4.4 8-4.4"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
              <path
                d="m4 15.1 8 4.4 8-4.4"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
              <path
                d="M9.5 6.9h5M11 5.7l-1.8 1 1.8 1M13 5.7l1.8 1-1.8 1"
                stroke="white"
                stroke-width="1.2"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
            <svg
              v-else-if="isDockerService(check.name)"
              class="size-5"
              viewBox="0 0 24 24"
              fill="none"
              aria-hidden="true"
            >
              <path
                d="M5.5 12.3h13.9c-.6 4-3.7 6.2-8.6 6.2-3.4 0-5.8-1.3-7-3.7-.2-.4.1-.9.6-.9h1.1v-1.6Z"
                fill="currentColor"
              />
              <path
                d="M6.1 8h2.6v2.5H6.1V8Zm3.3 0H12v2.5H9.4V8Zm3.3 0h2.6v2.5h-2.6V8ZM9.4 4.9H12v2.5H9.4V4.9Zm3.3 0h2.6v2.5h-2.6V4.9Zm3.3 3.1h2.6v2.5H16V8Z"
                fill="currentColor"
                opacity=".72"
              />
              <path
                d="M18.9 11.7c1.2-.1 2-.5 2.6-1.4.2 1.2-.2 2.2-1.1 2.9"
                stroke="currentColor"
                stroke-width="1.4"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
              <path d="M7.1 14.6h.1" stroke="white" stroke-width="2" stroke-linecap="round" />
            </svg>
            <component v-else :is="getServiceIcon(check.name)" class="size-4" />
          </div>
          <div class="min-w-0">
            <div class="truncate text-sm font-semibold">{{ check.name }}</div>
            <p v-if="check.description" class="mt-1 text-xs leading-relaxed text-muted-foreground">
              {{ check.description }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-2 md:justify-end">
          <Badge
            :variant="statusVariant(check.status)"
            class="uppercase"
            :class="statusBadgeClass(check.status)"
          >
            {{ check.status }}
          </Badge>
          <span class="text-xs text-muted-foreground">
            {{
              check.status === 'Healthy'
                ? t('admin.health.operational')
                : t('admin.health.actionRequired')
            }}
          </span>
        </div>
      </div>
    </div>

    <div v-if="!loading && !health" class="noctf-state-box py-20">
      <div
        class="size-16 rounded-full bg-destructive/10 flex items-center justify-center text-destructive mb-4"
      >
        <AlertTriangle class="size-8" />
      </div>
      <h3 class="text-xl font-bold">
        {{ t('admin.health.unavailableTitle') }}
      </h3>
      <p class="text-sm text-muted-foreground mt-2 max-w-xs">
        {{ t('admin.health.unavailableDescription') }}
      </p>
      <Button variant="outline" class="mt-6" @click="fetchHealth()">
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div
      class="flex items-center gap-2 px-1 text-[10px] font-bold uppercase tracking-widest text-muted-foreground opacity-50"
    >
      <RotateCw class="size-3 animate-spin" />
      {{ t('common.autoRefresh', { seconds: 15 }) }}
    </div>
  </div>
</template>
