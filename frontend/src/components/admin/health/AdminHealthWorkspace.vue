<script setup lang="ts">
import type { NoCtfapiEndpointsHealthResponse } from '@/api/generated/types.gen'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import {
  AlertTriangle,
  Clock,
  HeartPulse,
  RotateCw,
} from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { healthApi } from '@/api/noctf'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()

const health = ref<NoCtfapiEndpointsHealthResponse | null>(null)
const loading = ref(false)
const lastUpdated = ref<Date | null>(null)
let intervalId: ReturnType<typeof setInterval> | null = null

async function fetchHealth(silent = false) {
  if (!silent)
    loading.value = true

  try {
    health.value = await healthApi.get()
    lastUpdated.value = new Date()
  }
  catch {
    health.value = null
    toast.error(t('admin.health.checkFailed'))
  }
  finally {
    if (!silent)
      loading.value = false
  }
}

onMounted(() => {
  fetchHealth()
  intervalId = setInterval(fetchHealth, 15000, true)
})

onUnmounted(() => {
  if (intervalId)
    clearInterval(intervalId)
})

const overallHealthy = computed(() => health.value?.status?.toLowerCase() === 'ok')
</script>

<template>
  <div class="space-y-6">
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
        :class="overallHealthy ? 'bg-emerald-500/5 border-emerald-500/20' : 'bg-destructive/5 border-destructive/20'"
      >
        <div
          class="flex size-16 items-center justify-center rounded-xl shadow-lg transition-transform hover:scale-105"
          :class="overallHealthy ? 'bg-emerald-500 text-white' : 'bg-destructive text-white'"
        >
          <HeartPulse class="size-8" :class="{ 'animate-pulse': overallHealthy }" />
        </div>

        <div class="flex-1 text-center sm:text-left space-y-1">
          <h3 class="text-2xl font-black uppercase tracking-normal">
            {{ t('admin.health.overallStatus') }}: <span :class="overallHealthy ? 'text-emerald-500' : 'text-destructive'">{{ health.status }}</span>
          </h3>
          <p class="text-sm text-muted-foreground max-w-lg">
            {{ overallHealthy ? t('admin.health.operationalDescription') : t('admin.health.degradedDescription') }}
          </p>
        </div>
      </div>
    </div>

    <Skeleton v-if="loading && !health" class="h-32 rounded-xl" />

    <Card v-if="!loading && !health" class="flex flex-col items-center justify-center border-dashed py-20 text-center">
      <div class="size-16 rounded-full bg-destructive/10 flex items-center justify-center text-destructive mb-4">
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
    </Card>

    <div class="flex items-center gap-2 px-1 text-[10px] font-bold uppercase tracking-widest text-muted-foreground opacity-50">
      <RotateCw class="size-3 animate-spin" />
      {{ t('common.autoRefresh', { seconds: 15 }) }}
    </div>
  </div>
</template>
