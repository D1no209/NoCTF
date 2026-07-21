<script setup lang="ts">
import { computed } from 'vue'
import type { FunctionalComponent } from 'vue'
import {
  Activity,
  AlertTriangle,
  Clock,
  Database,
  HeartPulse,
  RotateCw,
  Server,
  Zap,
} from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminHealthCheck {
  name: string
  status: string
  description?: string
}

export interface CommandAdminHealthReport {
  status: string
  checks: CommandAdminHealthCheck[]
}

const props = defineProps<{
  health: CommandAdminHealthReport | null
  loading: boolean
  lastUpdated: Date | null
  operationMessage?: string
}>()

const emit = defineEmits<{
  refresh: []
}>()

const overallHealthy = computed(() => props.health?.status.toLowerCase() === 'healthy')

function statusTone(status: string): 'success' | 'warning' | 'danger' {
  const value = status.toLowerCase()
  if (value === 'healthy')
    return 'success'
  if (value === 'degraded' || value === 'warning')
    return 'warning'
  return 'danger'
}

function serviceIcon(name: string): FunctionalComponent {
  const value = name.toLowerCase()
  if (value.includes('database') || value.includes('pg') || value.includes('sql'))
    return Database
  if (value.includes('redis') || value.includes('cache'))
    return Zap
  if (value.includes('rabbit') || value.includes('bus'))
    return Server
  return Activity
}

function checkOperational(status: string) {
  return status.toLowerCase() === 'healthy'
}
</script>

<template>
  <section class="admin-health">
    <CommandPageHeader
      signal-label="Admin API / platform health"
      signal-tone="warning"
      title="System health"
      description="Probe the platform dependency checks with a 15 second auto refresh."
      :stat-icon="HeartPulse"
      :stat-value="props.health ? String(props.health.checks.length).padStart(2, '0') : '--'"
      stat-label="checks"
    >
      <div v-if="props.lastUpdated" class="admin-health__updated">
        <Clock class="size-3" />
        Last updated {{ props.lastUpdated.toLocaleTimeString() }}
      </div>
      <CommandButton
        :label="props.loading ? 'Checking' : 'Refresh'"
        tone="outline"
        :disabled="props.loading"
        @click="emit('refresh')"
      >
        <template #icon><RotateCw class="size-4" :class="{ 'animate-spin': props.loading }" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-health__message" role="alert">
      {{ props.operationMessage }}
    </p>

    <CommandPanel v-if="props.health" class="admin-health__banner" :tone="overallHealthy ? 'default' : 'warning'">
      <span class="admin-health__banner-icon" :class="{ 'admin-health__banner-icon--danger': !overallHealthy }">
        <HeartPulse class="size-7" :class="{ 'animate-pulse': overallHealthy }" />
      </span>
      <div class="admin-health__banner-body">
        <h2>
          Overall status:
          <span :class="overallHealthy ? 'admin-health__ok' : 'admin-health__bad'">{{ props.health.status }}</span>
        </h2>
        <p>{{ overallHealthy ? 'Every dependency check is operational.' : 'One or more dependency checks require operator attention.' }}</p>
      </div>
      <CommandBadge :label="`${props.health.checks.length} components`" :tone="overallHealthy ? 'success' : 'danger'" />
    </CommandPanel>

    <CommandPanel v-if="props.loading && !props.health" class="admin-health__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Running the platform health checks.</p>
    </CommandPanel>

    <div v-if="props.health" class="admin-health__grid">
      <CommandPanel v-for="check in props.health.checks" :key="check.name" class="admin-health__card">
        <div class="admin-health__card-head">
          <span class="admin-health__card-identity">
            <span class="admin-health__card-icon">
              <component :is="serviceIcon(check.name)" class="size-4" />
            </span>
            <strong>{{ check.name }}</strong>
          </span>
          <CommandBadge :label="check.status" :tone="statusTone(check.status)" />
        </div>
        <p v-if="check.description" class="admin-health__card-description">{{ check.description }}</p>
        <span class="admin-health__card-foot">
          <span class="admin-health__card-dot" :class="checkOperational(check.status) ? 'admin-health__card-dot--ok' : 'admin-health__card-dot--bad'" aria-hidden="true" />
          {{ checkOperational(check.status) ? 'Operational' : 'Action required' }}
        </span>
      </CommandPanel>
    </div>

    <CommandPanel v-if="!props.loading && !props.health" class="admin-health__state admin-health__state--unavailable" tone="warning">
      <AlertTriangle class="size-8 text-[var(--v2-danger)]" />
      <h2>Health endpoint unavailable</h2>
      <p>The platform did not return a health report. Verify the API service is running.</p>
      <CommandButton label="Retry" tone="outline" @click="emit('refresh')" />
    </CommandPanel>

    <div class="admin-health__autorefresh">
      <RotateCw class="size-3 animate-spin" />
      Auto refresh every 15 seconds
    </div>
  </section>
</template>

<style scoped>
.admin-health { display: grid; gap: 16px; }
.admin-health__updated { display: flex; align-items: center; gap: 6px; border-radius: 999px; padding: 5px 12px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 10px; letter-spacing: 0.06em; }
.admin-health__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-health__banner { display: flex; flex-wrap: wrap; align-items: center; gap: 18px; padding: 20px; }
.admin-health__banner-icon { display: grid; width: 56px; height: 56px; flex-shrink: 0; place-items: center; border-radius: 14px; color: #ffffff; background: var(--v2-cyan); box-shadow: var(--v2-raised); }
.admin-health__banner-icon--danger { background: var(--v2-danger); }
.admin-health__banner-body { display: grid; flex: 1; min-width: 220px; gap: 4px; }
.admin-health__banner-body h2 { margin: 0; color: var(--v2-text); font-size: 18px; font-weight: 600; }
.admin-health__banner-body p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-health__ok { color: var(--v2-cyan); }
.admin-health__bad { color: var(--v2-danger); }
.admin-health__state { display: grid; min-height: 160px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-health__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-health__state p { margin: 0; max-width: 380px; color: var(--v2-text-muted); font-size: 13px; }
.admin-health__grid { display: grid; gap: 14px; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); }
.admin-health__card { display: grid; align-content: start; gap: 12px; padding: 16px; }
.admin-health__card-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.admin-health__card-identity { display: flex; min-width: 0; align-items: center; gap: 10px; }
.admin-health__card-identity strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-health__card-icon { display: grid; width: 32px; height: 32px; flex-shrink: 0; place-items: center; border-radius: 10px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-health__card-description { margin: 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.5; }
.admin-health__card-foot { display: flex; align-items: center; gap: 6px; color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.06em; }
.admin-health__card-dot { width: 6px; height: 6px; border-radius: 999px; }
.admin-health__card-dot--ok { background: var(--v2-cyan); }
.admin-health__card-dot--bad { background: var(--v2-danger); }
.admin-health__autorefresh { display: flex; align-items: center; gap: 7px; padding: 0 4px; color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
</style>
