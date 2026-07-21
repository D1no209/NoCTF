<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { Activity, ArrowDown, Pause, Play, Search, Terminal, Trash2 } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'

export interface CommandAdminLogEntry {
  level: string
  message: string
  source: string
  timestamp: string
}

const props = defineProps<{
  logs: CommandAdminLogEntry[]
  paused: boolean
  connected: boolean
  operationMessage?: string
}>()

const emit = defineEmits<{
  togglePause: []
  clear: []
}>()

const logLevels = ['All', 'Information', 'Warning', 'Error'] as const
const levelFilter = ref<(typeof logLevels)[number]>('All')
const searchTerm = ref('')
const logContainer = ref<HTMLElement | null>(null)

const filteredLogs = computed(() => {
  let result = props.logs
  if (levelFilter.value !== 'All')
    result = result.filter(log => log.level === levelFilter.value)
  if (searchTerm.value) {
    const search = searchTerm.value.toLowerCase()
    result = result.filter(log =>
      log.message.toLowerCase().includes(search) || log.source.toLowerCase().includes(search))
  }
  return result
})

function levelClass(level: string) {
  if (level === 'Warning')
    return 'admin-logs__message--warning'
  if (level === 'Error')
    return 'admin-logs__message--error'
  if (level === 'Information')
    return 'admin-logs__message--info'
  return ''
}

function formatTime(timestamp: string) {
  const date = new Date(timestamp)
  if (Number.isNaN(date.getTime()))
    return '--:--:--.---'
  return `${date.toLocaleTimeString('en-GB', { hour12: false })}.${date.getMilliseconds().toString().padStart(3, '0')}`
}

async function scrollToBottom(force = false) {
  if (props.paused && !force)
    return
  await nextTick()
  if (logContainer.value)
    logContainer.value.scrollTop = logContainer.value.scrollHeight
}

watch(filteredLogs, () => {
  void scrollToBottom()
})
</script>

<template>
  <section class="admin-logs">
    <CommandPanel class="admin-logs__toolbar">
      <div class="admin-logs__identity">
        <span class="admin-logs__identity-icon">
          <Terminal class="size-5" />
        </span>
        <div class="admin-logs__identity-body">
          <h1>Live system logs</h1>
          <span class="admin-logs__stream">
            <span class="admin-logs__stream-dot" :class="{ 'admin-logs__stream-dot--on': props.connected }" aria-hidden="true" />
            {{ props.connected ? 'Streaming' : 'Disconnected' }}
          </span>
        </div>
      </div>

      <div class="admin-logs__controls">
        <div class="admin-logs__search">
          <Search class="size-4 text-[var(--v2-text-faint)]" />
          <CommandInput v-model="searchTerm" type="search" label="Filter logs" placeholder="Filter by message or source" />
        </div>
        <div class="admin-logs__levels" role="tablist" aria-label="Log level">
          <button
            v-for="level in logLevels"
            :key="level"
            type="button"
            class="admin-logs__level"
            :class="{ 'admin-logs__level--active': levelFilter === level }"
            @click="levelFilter = level"
          >
            {{ level }}
          </button>
        </div>
        <CommandButton
          :label="props.paused ? 'Resume' : 'Pause'"
          tone="outline"
          @click="emit('togglePause')"
        >
          <template #icon>
            <Play v-if="props.paused" class="size-4" />
            <Pause v-else class="size-4" />
          </template>
        </CommandButton>
        <CommandButton label="Clear" tone="ghost" class="admin-logs__clear" @click="emit('clear')">
          <template #icon><Trash2 class="size-4" /></template>
        </CommandButton>
      </div>
    </CommandPanel>

    <p v-if="props.operationMessage" class="admin-logs__message" role="alert">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-logs__terminal">
      <div class="admin-logs__terminal-head">
        <span class="admin-logs__terminal-title">
          <Activity class="size-3" />
          stdout.log - {{ filteredLogs.length }} lines
        </span>
        <button v-if="props.paused" type="button" class="admin-logs__jump" @click="scrollToBottom(true)">
          <ArrowDown class="size-3" />
          Jump to end
        </button>
      </div>
      <div ref="logContainer" class="admin-logs__terminal-body">
        <div v-if="filteredLogs.length === 0" class="admin-logs__waiting">
          <Terminal class="size-8" />
          <p>Waiting for log entries.</p>
        </div>
        <div v-for="(entry, index) in filteredLogs" :key="index" class="admin-logs__line">
          <span class="admin-logs__time">{{ formatTime(entry.timestamp) }}</span>
          <span class="admin-logs__source">[{{ entry.source }}]</span>
          <span class="admin-logs__text" :class="levelClass(entry.level)">{{ entry.message }}</span>
        </div>
      </div>
    </CommandPanel>
  </section>
</template>

<style scoped>
.admin-logs { display: grid; gap: 14px; }
.admin-logs__toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 14px; padding: 14px 16px; }
.admin-logs__identity { display: flex; align-items: center; gap: 12px; }
.admin-logs__identity-icon { display: grid; width: 42px; height: 42px; place-items: center; border-radius: 12px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-logs__identity-body { display: grid; gap: 4px; }
.admin-logs__identity-body h1 { margin: 0; color: var(--v2-text); font-size: 16px; font-weight: 600; line-height: 1; }
.admin-logs__stream { display: flex; align-items: center; gap: 7px; color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-logs__stream-dot { width: 7px; height: 7px; border-radius: 999px; background: var(--v2-text-faint); }
.admin-logs__stream-dot--on { background: var(--v2-cyan); animation: admin-logs-pulse 1.6s ease-in-out infinite; }
@keyframes admin-logs-pulse { 50% { opacity: 0.4; } }
.admin-logs__controls { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.admin-logs__search { display: grid; width: min(260px, 100%); grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 9px; }
.admin-logs__levels { display: flex; gap: 4px; border-radius: 12px; padding: 4px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-logs__level { border: 0; border-radius: 9px; padding: 6px 11px; color: var(--v2-text-muted); background: transparent; cursor: pointer; font-family: inherit; font-size: 10px; font-weight: 600; letter-spacing: 0.05em; }
.admin-logs__level--active { color: var(--v2-primary); background: var(--v2-canvas); box-shadow: var(--v2-raised-sm); }
.admin-logs__clear { color: var(--v2-danger); }
.admin-logs__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-logs__terminal { display: grid; overflow: hidden; padding: 0; }
.admin-logs__terminal-head { display: flex; align-items: center; justify-content: space-between; padding: 10px 16px; box-shadow: var(--v2-inset); }
.admin-logs__terminal-title { display: flex; align-items: center; gap: 7px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 10px; }
.admin-logs__jump { display: flex; align-items: center; gap: 5px; border: 0; padding: 0; color: var(--v2-primary); background: transparent; cursor: pointer; font-family: inherit; font-size: 10px; font-weight: 600; }
.admin-logs__terminal-body { display: grid; align-content: start; height: min(560px, 62dvh); overflow-y: auto; padding: 12px 16px; font-family: var(--v2-font-mono); font-size: 11px; }
.admin-logs__waiting { display: grid; height: 100%; min-height: 220px; place-content: center; justify-items: center; gap: 10px; color: var(--v2-text-faint); }
.admin-logs__waiting p { margin: 0; font-size: 12px; }
.admin-logs__line { display: flex; gap: 14px; border-radius: 6px; padding: 2px 6px; }
.admin-logs__line:hover { background: var(--v2-surface); }
.admin-logs__time { width: 86px; flex-shrink: 0; color: var(--v2-text-faint); user-select: none; }
.admin-logs__source { overflow: hidden; width: 110px; flex-shrink: 0; color: var(--v2-text-faint); font-style: italic; text-overflow: ellipsis; white-space: nowrap; }
.admin-logs__text { min-width: 0; color: var(--v2-text); line-height: 1.55; white-space: pre-wrap; word-break: break-all; }
.admin-logs__text.admin-logs__message--warning { color: var(--v2-warning, var(--v2-primary)); }
.admin-logs__text.admin-logs__message--error { color: var(--v2-danger); }
.admin-logs__text.admin-logs__message--info { color: var(--v2-cyan); }
</style>
