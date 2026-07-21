<script setup lang="ts">
import { Activity, Box, CheckCircle2, RefreshCw, ServerCrash } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminInfrastructure {
  runnerProvider: string
  runnerBaseUrl: string | null
  runnerReachable: boolean
  runnerInfo: [string, unknown][]
  kubernetes: [string, unknown][]
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  infrastructure: CommandAdminInfrastructure | null
  refreshing: boolean
}>()

const emit = defineEmits<{
  refresh: []
  retry: []
}>()

function formatValue(value: unknown) {
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}
</script>

<template>
  <section class="admin-infrastructure">
    <CommandPageHeader
      signal-label="Admin API / runtime infrastructure"
      signal-tone="warning"
      title="Infrastructure"
      description="Inspect the container runner connection and cluster defaults."
      :stat-icon="Box"
      :stat-value="props.infrastructure?.runnerProvider ?? '--'"
      stat-label="runner"
    >
      <CommandButton
        :label="props.refreshing ? 'Refreshing' : 'Refresh'"
        tone="outline"
        :disabled="props.refreshing"
        @click="emit('refresh')"
      >
        <template #icon><RefreshCw class="size-4" :class="{ 'animate-spin': props.refreshing }" /></template>
      </CommandButton>
    </CommandPageHeader>

    <CommandPanel v-if="props.state === 'error'" class="admin-infrastructure__state" tone="warning">
      <h2>Unable to load infrastructure</h2>
      <p>{{ props.errorMessage || 'The service did not return infrastructure metadata.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'loading' || !props.infrastructure" class="admin-infrastructure__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Probing the runtime infrastructure.</p>
    </CommandPanel>

    <template v-else>
      <div class="admin-infrastructure__stats">
        <CommandPanel class="admin-infrastructure__stat">
          <Box class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-infrastructure__stat-body">
            <span class="admin-infrastructure__stat-label">Runner</span>
            <strong>{{ props.infrastructure.runnerProvider }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-infrastructure__stat">
          <CheckCircle2 v-if="props.infrastructure.runnerReachable" class="size-5 text-[var(--v2-cyan)]" />
          <ServerCrash v-else class="size-5 text-[var(--v2-danger)]" />
          <span class="admin-infrastructure__stat-body">
            <span class="admin-infrastructure__stat-label">Reachability</span>
            <strong>{{ props.infrastructure.runnerReachable ? 'Connected' : 'Unavailable' }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-infrastructure__stat">
          <Activity class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-infrastructure__stat-body">
            <span class="admin-infrastructure__stat-label">Runner URL</span>
            <strong class="admin-infrastructure__stat-url">{{ props.infrastructure.runnerBaseUrl || 'Not configured' }}</strong>
          </span>
        </CommandPanel>
      </div>

      <div class="admin-infrastructure__columns">
        <CommandPanel class="admin-infrastructure__panel">
          <div class="admin-infrastructure__panel-head">
            <h2>Runner details</h2>
            <CommandBadge :label="String(props.infrastructure.runnerInfo.length)" tone="info" />
          </div>
          <ul v-if="props.infrastructure.runnerInfo.length" class="admin-infrastructure__fields">
            <li v-for="[key, value] in props.infrastructure.runnerInfo" :key="key">
              <span class="admin-infrastructure__field-key">{{ key }}</span>
              <code class="admin-infrastructure__field-value">{{ formatValue(value) }}</code>
            </li>
          </ul>
          <p v-else class="admin-infrastructure__empty">No runner metadata reported.</p>
        </CommandPanel>

        <CommandPanel class="admin-infrastructure__panel">
          <div class="admin-infrastructure__panel-head">
            <h2>Cluster defaults</h2>
            <CommandBadge :label="String(props.infrastructure.kubernetes.length)" tone="info" />
          </div>
          <ul v-if="props.infrastructure.kubernetes.length" class="admin-infrastructure__fields">
            <li v-for="[key, value] in props.infrastructure.kubernetes" :key="key">
              <span class="admin-infrastructure__field-key">{{ key }}</span>
              <code class="admin-infrastructure__field-value">{{ formatValue(value) }}</code>
            </li>
          </ul>
          <p v-else class="admin-infrastructure__empty">No cluster defaults reported.</p>
        </CommandPanel>
      </div>
    </template>
  </section>
</template>

<style scoped>
.admin-infrastructure { display: grid; gap: 16px; }
.admin-infrastructure__state { display: grid; min-height: 160px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-infrastructure__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-infrastructure__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-infrastructure__stats { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-infrastructure__stat { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.admin-infrastructure__stat-body { display: grid; min-width: 0; gap: 2px; }
.admin-infrastructure__stat-label { color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-infrastructure__stat-body strong { overflow: hidden; color: var(--v2-text); font-size: 15px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-infrastructure__stat-url { font-family: var(--v2-font-mono); font-size: 12px !important; }
.admin-infrastructure__columns { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
.admin-infrastructure__panel { display: grid; align-content: start; gap: 12px; padding: 18px; }
.admin-infrastructure__panel-head { display: flex; align-items: center; justify-content: space-between; }
.admin-infrastructure__panel-head h2 { margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; }
.admin-infrastructure__fields { display: grid; gap: 8px; margin: 0; padding: 0; list-style: none; }
.admin-infrastructure__fields li { display: flex; align-items: center; justify-content: space-between; gap: 14px; border-radius: 10px; padding: 9px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-infrastructure__field-key { color: var(--v2-text-muted); font-size: 12px; }
.admin-infrastructure__field-value { overflow: hidden; max-width: 60%; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.admin-infrastructure__empty { margin: 0; border-radius: 10px; padding: 16px 12px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; text-align: center; }
</style>
