<script setup lang="ts">
import { ArrowLeft, Boxes, Flag, Play, RefreshCw, RotateCcw, Square, Trash2 } from 'lucide-vue-next'
import { computed } from 'vue'
import type { CommandAwdChallenge } from './awd-contract'
import CommandAwdFlagConsole from './CommandAwdFlagConsole.vue'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandPenetrationNode {
  id: string
  name: string
  image: string
  isEntry: boolean
  isInternal: boolean
}

export interface CommandPenetrationFlag {
  id: string
  name: string
  nodeName?: string | null
  stage: number
  score: number
  visible: boolean
  solved: boolean
  hintAfterSolved?: string | null
}

export interface CommandPenetrationInstance {
  id?: string | null
  status: string
  entryHost?: string | null
  entryPort?: number | null
  entryUrl?: string | null
  resetCount: number
  resetLimit: number
  expiresAt?: string | null
  cooldownUntil?: string | null
  lastError?: string | null
  ports?: Record<string, number>
}

export interface CommandPenetrationDetail {
  topology?: {
    name: string
    description?: string | null
    nodes: CommandPenetrationNode[]
    flags: CommandPenetrationFlag[]
    config?: { allowReset?: boolean }
  } | null
  instance: CommandPenetrationInstance
  totalStageCount: number
  solvedStageCount: number
  totalScore: number
}

const props = withDefaults(defineProps<{
  state: 'invalid' | 'loading' | 'error' | 'ready'
  errorMessage?: string
  challenges: CommandAwdChallenge[]
  selectedChallengeId: string
  detail?: CommandPenetrationDetail
  detailLoading?: boolean
  actionPending?: string
  flag: string
  submitPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>(), {
  errorMessage: undefined,
  detail: undefined,
  detailLoading: false,
  actionPending: '',
  operationMessage: '',
  operationTone: 'success',
})

const emit = defineEmits<{
  back: []
  retry: []
  action: [name: 'start' | 'stop' | 'reset' | 'destroy']
  submit: []
  'update:selectedChallengeId': [value: string]
  'update:flag': [value: string]
}>()

const instanceStatus = computed(() => (props.detail?.instance.status ?? 'unknown').toLowerCase())
const instanceSignal = computed(() => {
  if (instanceStatus.value === 'running')
    return { label: 'Instance running', tone: 'success' as const }
  if (instanceStatus.value === 'starting' || instanceStatus.value === 'creating')
    return { label: 'Instance starting', tone: 'info' as const }
  if (instanceStatus.value === 'error' || instanceStatus.value === 'failed')
    return { label: 'Instance fault', tone: 'danger' as const }
  return { label: `Instance ${instanceStatus.value}`, tone: 'warning' as const }
})
const hasInstance = computed(() => Boolean(props.detail?.instance.id))
const isRunning = computed(() => instanceStatus.value === 'running')
const allowReset = computed(() => props.detail?.topology?.config?.allowReset !== false)
const entryAddress = computed(() => {
  const instance = props.detail?.instance
  if (!instance?.entryHost)
    return 'Not exposed'
  return instance.entryPort ? `${instance.entryHost}:${instance.entryPort}` : instance.entryHost
})
const expiresText = computed(() => {
  const value = props.detail?.instance.expiresAt
  if (!value)
    return 'No expiry'
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp))
    return 'No expiry'
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(timestamp))
})
const visibleFlags = computed(() => (props.detail?.topology?.flags ?? []).filter(flag => flag.visible))

function formatPorts(ports?: Record<string, number>) {
  const entries = Object.entries(ports ?? {})
  if (!entries.length)
    return 'None mapped'
  return entries.map(([container, host]) => `${container} → ${host}`).join('  ·  ')
}
</script>

<template>
  <section v-if="props.state === 'loading'" class="pen-workspace__state">
    <CommandPanel class="pen-workspace__skeleton" aria-busy="true"><span /><span /><span /></CommandPanel>
  </section>

  <section v-else-if="props.state === 'invalid' || props.state === 'error'" class="pen-workspace__state">
    <CommandPanel class="pen-workspace__error" tone="warning">
      <div>
        <CommandSignal :label="props.state === 'invalid' ? 'Invalid competition identifier' : 'Range unavailable'" tone="danger" />
        <h1>Unable to load the penetration workspace</h1>
        <p>{{ props.errorMessage || 'The range service did not return usable topology data.' }}</p>
      </div>
      <CommandButton v-if="props.state === 'error'" label="Retry" @click="emit('retry')">
        <template #icon><RefreshCw class="size-4" /></template>
      </CommandButton>
    </CommandPanel>
  </section>

  <section v-else class="pen-workspace">
    <header class="pen-workspace__heading">
      <div class="pen-workspace__title">
        <CommandButton label="Back" tone="ghost" @click="emit('back')">
          <template #icon><ArrowLeft class="size-4" /></template>
        </CommandButton>
        <div>
          <CommandSignal label="Penetration range operations" tone="info" />
          <h1>Range control room</h1>
        </div>
      </div>
      <CommandBadge
        v-if="props.detail"
        :label="`Stages ${props.detail.solvedStageCount}/${props.detail.totalStageCount} · ${props.detail.totalScore} pts`"
        tone="primary"
      />
    </header>

    <div class="pen-workspace__grid">
      <div class="pen-workspace__side">
        <CommandPanel class="pen-instance">
          <header class="pen-instance__header">
            <div>
              <CommandSignal label="Range instance" tone="info" />
              <h2>Instance</h2>
            </div>
            <CommandSignal :label="instanceSignal.label" :tone="instanceSignal.tone" />
          </header>

          <dl class="pen-instance__facts">
            <div><dt>Entry</dt><dd>{{ entryAddress }}</dd></div>
            <div><dt>Entry URL</dt><dd>{{ props.detail?.instance.entryUrl || 'Not exposed' }}</dd></div>
            <div><dt>Resets</dt><dd>{{ props.detail?.instance.resetCount ?? 0 }} / {{ props.detail?.instance.resetLimit ?? 0 }}</dd></div>
            <div><dt>Expires</dt><dd>{{ expiresText }}</dd></div>
            <div class="pen-instance__facts--wide"><dt>Ports</dt><dd>{{ formatPorts(props.detail?.instance.ports) }}</dd></div>
          </dl>

          <p v-if="props.detail?.instance.lastError" class="pen-instance__fault">
            {{ props.detail.instance.lastError }}
          </p>

          <div class="pen-instance__actions">
            <CommandButton
              :label="props.actionPending === 'start' ? 'Starting' : 'Start'"
              :disabled="isRunning || Boolean(props.actionPending)"
              @click="emit('action', 'start')"
            >
              <template #icon><Play class="size-4" /></template>
            </CommandButton>
            <CommandButton
              :label="props.actionPending === 'stop' ? 'Stopping' : 'Stop'"
              tone="outline"
              :disabled="!isRunning || Boolean(props.actionPending)"
              @click="emit('action', 'stop')"
            >
              <template #icon><Square class="size-4" /></template>
            </CommandButton>
            <CommandButton
              :label="props.actionPending === 'reset' ? 'Resetting' : 'Reset'"
              tone="ghost"
              :disabled="!allowReset || !hasInstance || Boolean(props.actionPending)"
              @click="emit('action', 'reset')"
            >
              <template #icon><RotateCcw class="size-4" /></template>
            </CommandButton>
            <CommandButton
              :label="props.actionPending === 'destroy' ? 'Destroying' : 'Destroy'"
              tone="ghost"
              :disabled="!hasInstance || Boolean(props.actionPending)"
              @click="emit('action', 'destroy')"
            >
              <template #icon><Trash2 class="size-4" /></template>
            </CommandButton>
          </div>
        </CommandPanel>

        <CommandAwdFlagConsole
          :challenges="props.challenges"
          :selected-challenge-id="props.selectedChallengeId"
          :flag="props.flag"
          :pending="props.submitPending"
          :operation-message="props.operationMessage"
          :operation-tone="props.operationTone"
          @update:selected-challenge-id="emit('update:selectedChallengeId', $event)"
          @update:flag="emit('update:flag', $event)"
          @submit="emit('submit')"
        />
      </div>

      <CommandPanel class="pen-topology">
        <header class="pen-topology__header">
          <div>
            <CommandSignal :label="props.detail?.topology?.name || 'Topology'" tone="info" />
            <h2>Range topology</h2>
          </div>
          <Boxes class="size-4 text-[var(--v2-primary)]" />
        </header>

        <p v-if="props.detail?.topology?.description" class="pen-topology__description">
          {{ props.detail.topology.description }}
        </p>

        <div v-if="props.detailLoading" class="pen-topology__loading">
          <span /><span /><span />
        </div>

        <template v-else-if="props.detail?.topology">
          <div class="pen-topology__nodes">
            <div v-for="node in props.detail.topology.nodes" :key="node.id" class="pen-node">
              <div class="pen-node__meta">
                <strong>{{ node.name }}</strong>
                <span>{{ node.image }}</span>
              </div>
              <div class="pen-node__tags">
                <CommandBadge v-if="node.isEntry" label="Entry" tone="primary" />
                <CommandBadge :label="node.isInternal ? 'Internal' : 'Exposed'" :tone="node.isInternal ? 'warning' : 'info'" />
              </div>
            </div>
          </div>

          <div class="pen-topology__flags">
            <h3><Flag class="size-3.5" /> Staged flags</h3>
            <ul>
              <li v-for="flag in visibleFlags" :key="flag.id" class="pen-flag">
                <span class="pen-flag__stage">S{{ flag.stage }}</span>
                <div class="pen-flag__meta">
                  <strong>{{ flag.name }}</strong>
                  <span>{{ flag.nodeName || 'any node' }} · {{ flag.score }} pts</span>
                  <p v-if="flag.solved && flag.hintAfterSolved">{{ flag.hintAfterSolved }}</p>
                </div>
                <CommandBadge :label="flag.solved ? 'Solved' : 'Open'" :tone="flag.solved ? 'success' : 'default'" />
              </li>
            </ul>
          </div>
        </template>

        <div v-else class="pen-topology__empty">
          <CommandSignal label="No challenge selected" tone="warning" />
          <p>Select a penetration challenge to inspect its range topology.</p>
        </div>
      </CommandPanel>
    </div>
  </section>
</template>

<style scoped>
.pen-workspace,
.pen-workspace__state { display: grid; gap: 16px; }

.pen-workspace__skeleton { display: grid; min-height: 240px; grid-template-rows: 18px 1fr 1fr; gap: 14px; padding: 22px; }
.pen-workspace__skeleton span { display: block; border-radius: 12px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); animation: pen-pulse 1.1s ease-in-out infinite alternate; }
.pen-workspace__skeleton span:nth-child(1) { width: 24%; }

.pen-workspace__error { display: flex; min-height: 210px; align-items: center; justify-content: space-between; gap: 20px; padding: 26px; }
.pen-workspace__error h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 19px; font-weight: 600; }
.pen-workspace__error p { max-width: 650px; margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }

.pen-workspace__heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 20px; padding: 4px 2px 0; }
.pen-workspace__title { display: flex; min-width: 0; align-items: center; gap: 14px; }
.pen-workspace__title h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 600; letter-spacing: -0.01em; }

.pen-workspace__grid { display: grid; grid-template-columns: minmax(340px, 0.9fr) minmax(0, 1.1fr); align-items: start; gap: 16px; }
.pen-workspace__side { display: grid; min-width: 0; gap: 16px; }

.pen-instance { display: grid; gap: 14px; padding: 16px; }
.pen-instance__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
.pen-instance__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }

.pen-instance__facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; margin: 0; }
.pen-instance__facts > div { display: grid; gap: 5px; border-radius: 10px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.pen-instance__facts--wide { grid-column: 1 / -1; }
.pen-instance__facts dt { color: var(--v2-text-faint); font-size: 10px; font-weight: 600; letter-spacing: 0.05em; }
.pen-instance__facts dd { overflow: hidden; margin: 0; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 11px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }

.pen-instance__fault { margin: 0; border-radius: 12px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); color: var(--v2-danger); font-size: 12px; line-height: 1.45; }
.pen-instance__actions { display: flex; flex-wrap: wrap; gap: 8px; }

.pen-topology { display: grid; gap: 14px; padding: 16px; }
.pen-topology__header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
.pen-topology__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.pen-topology__description { margin: 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }

.pen-topology__loading { display: grid; gap: 10px; }
.pen-topology__loading span { display: block; height: 52px; border-radius: 12px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); animation: pen-pulse 1.1s ease-in-out infinite alternate; }

.pen-topology__nodes { display: grid; gap: 8px; }
.pen-node {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-radius: 12px;
  padding: 11px 13px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}
.pen-node__meta { display: grid; min-width: 0; gap: 3px; }
.pen-node__meta strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.pen-node__meta span { overflow: hidden; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.pen-node__tags { display: flex; flex: none; gap: 6px; }

.pen-topology__flags { display: grid; gap: 10px; margin-top: 4px; }
.pen-topology__flags h3 { display: inline-flex; align-items: center; gap: 7px; margin: 0; color: var(--v2-text-muted); font-size: 12px; font-weight: 600; letter-spacing: 0.04em; }
.pen-topology__flags ul { display: grid; gap: 8px; margin: 0; padding: 0; list-style: none; }

.pen-flag {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
  border-radius: 12px;
  padding: 11px 13px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}
.pen-flag__stage {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: 999px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
  font-family: var(--v2-font-mono);
  font-size: 11px;
  font-weight: 600;
}
.pen-flag__meta { display: grid; min-width: 0; gap: 3px; }
.pen-flag__meta strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.pen-flag__meta span { color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; }
.pen-flag__meta p { margin: 3px 0 0; color: var(--v2-cyan); font-size: 11px; line-height: 1.45; }

.pen-topology__empty { display: grid; min-height: 160px; align-content: center; justify-items: start; gap: 10px; }
.pen-topology__empty p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }

@keyframes pen-pulse {
  to { opacity: 0.45; }
}

@media (max-width: 1080px) {
  .pen-workspace__grid { grid-template-columns: 1fr; }
}

@media (max-width: 700px) {
  .pen-workspace__heading { align-items: flex-start; flex-direction: column; }
}
</style>
