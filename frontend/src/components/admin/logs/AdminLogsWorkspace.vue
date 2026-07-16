<script setup lang="ts">
import { Activity, ArrowDown, Pause, Play, Search, Terminal, Trash2 } from 'lucide-vue-next'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'

interface LogEntryDto {
  level: string
  message: string
  source: string
  timestamp: string
}

const SYSTEM_COMPETITION_ID = '00000000-0000-0000-0000-000000000001'

const { t } = useI18n()
const auth = useAuthStore()
const logs = ref<LogEntryDto[]>([])
const paused = ref(false)
const logLevels = ['All', 'Information', 'Warning', 'Error'] as const
const levelFilter = ref<'All' | 'Information' | 'Warning' | 'Error'>('All')
const searchTerm = ref('')
const logContainer = ref<HTMLElement | null>(null)

const filteredLogs = computed(() => {
  let result = logs.value
  if (levelFilter.value !== 'All')
    result = result.filter(log => log.level === levelFilter.value)

  if (searchTerm.value) {
    const search = searchTerm.value.toLowerCase()
    result = result.filter(log =>
      log.message.toLowerCase().includes(search) || log.source.toLowerCase().includes(search),
    )
  }
  return result
})

function levelColor(level: string) {
  if (level === 'Warning')
    return 'text-warning'
  if (level === 'Error')
    return 'text-danger'
  if (level === 'Information')
    return 'text-success'
  return 'text-status-neutral'
}

function formatTime(timestamp: string) {
  const date = new Date(timestamp)
  return `${date.toLocaleTimeString('en-GB', { hour12: false })}.${date.getMilliseconds().toString().padStart(3, '0')}`
}

async function scrollToBottom(force = false) {
  if (paused.value && !force)
    return
  await nextTick()
  if (logContainer.value)
    logContainer.value.scrollTop = logContainer.value.scrollHeight
}

watch(filteredLogs, () => scrollToBottom())

const signalR = useSignalR({
  hubUrl: `/hubs/monitor?competitionId=${SYSTEM_COMPETITION_ID}`,
  accessToken: () => auth.accessToken,
})

signalR.connection.value?.on('ReceiveLogEntry', (entry: LogEntryDto) => {
  if (paused.value)
    return
  logs.value.push(entry)
  if (logs.value.length > 1500)
    logs.value.splice(0, logs.value.length - 1500)
})

async function fetchHistorical() {
  try {
    const history = await adminApi.logs<LogEntryDto[]>()
    logs.value = [...history, ...logs.value].slice(-1500)
  }
  catch {
    toast.error(t('admin.logs.loadHistoryError'))
  }
}

function clearLogs() {
  logs.value = []
  toast.success(t('admin.logs.clearSuccess'))
}

onMounted(async () => {
  await fetchHistorical()
  await signalR.start()
  await scrollToBottom(true)
})
</script>

<template>
  <div class="flex min-h-[calc(100dvh-7rem)] flex-col gap-4 sm:h-[calc(100vh-8rem)]">
    <div class="noctf-toolbar">
      <div class="flex items-center gap-3 sm:gap-4">
        <div class="flex size-10 items-center justify-center rounded-md border border-border bg-muted text-primary">
          <Terminal class="size-5" />
        </div>
        <div>
          <h2 class="text-lg font-bold leading-none tracking-tight">
            {{ t('admin.logs.subtitle') }}
          </h2>
          <div class="mt-1.5 flex items-center gap-2">
            <div
              class="size-2 animate-pulse rounded-full"
              :class="signalR.isConnected.value ? 'bg-success' : 'bg-status-neutral'"
            />
            <span class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
              {{ signalR.isConnected.value ? t('admin.logs.streaming') : t('common.disconnected') }}
            </span>
          </div>
        </div>
      </div>

      <div class="flex w-full flex-wrap items-center gap-2 md:w-auto">
        <div class="relative w-full sm:w-48">
          <Search class="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            v-model="searchTerm"
            :placeholder="t('admin.logs.filterPlaceholder')"
            class="h-8 border-none bg-muted/50 pl-8 text-xs"
          />
        </div>

        <div class="mx-1 hidden h-4 w-px bg-border sm:block" />

        <div class="noctf-scrollbar flex max-w-full overflow-x-auto rounded-md border border-border/80 bg-muted/50 p-1">
          <button
            v-for="level in logLevels"
            :key="level"
            class="rounded px-2.5 py-1 text-[10px] font-bold uppercase tracking-tighter transition-[background-color,color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-out-quint)]"
            :class="levelFilter === level ? 'bg-background text-foreground' : 'text-muted-foreground hover:text-foreground'"
            @click="levelFilter = level"
          >
            {{ level }}
          </button>
        </div>

        <div class="mx-1 hidden h-4 w-px bg-border sm:block" />

        <Button
          size="sm"
          variant="outline"
          class="gap-2 border-dashed sm:h-8"
          :class="paused ? 'border-danger/45 text-danger hover:bg-danger-muted/45' : ''"
          @click="paused = !paused"
        >
          <component :is="paused ? Play : Pause" class="size-3.5" />
          <span class="text-[10px] font-bold uppercase">
            {{ paused ? t('admin.logs.resume') : t('admin.logs.pause') }}
          </span>
        </Button>

        <Button
          size="sm"
          variant="ghost"
          class="size-11 p-0 text-muted-foreground hover:text-danger sm:size-8 sm:h-8"
          @click="clearLogs"
        >
          <Trash2 class="size-4" />
        </Button>
      </div>
    </div>

    <div class="noctf-terminal group relative flex min-h-0 flex-1 flex-col overflow-hidden">
      <div class="flex h-8 shrink-0 items-center justify-between border-b border-sidebar-border bg-sidebar-accent/40 px-4">
        <div class="flex items-center gap-1.5">
          <span class="flex items-center gap-1.5 font-mono text-[10px] text-sidebar-foreground/60">
            <Activity class="size-3" />
            stdout.log - {{ t('admin.logs.lineCount', { count: filteredLogs.length }) }}
          </span>
        </div>
        <button
          v-if="paused"
          class="flex items-center gap-1 text-[10px] font-bold text-primary hover:underline"
          @click="scrollToBottom(true)"
        >
          <ArrowDown class="size-3" /> {{ t('admin.logs.jumpToEnd') }}
        </button>
      </div>

      <div
        ref="logContainer"
        class="noctf-scrollbar flex-1 overflow-y-auto p-4 font-mono text-[11px] selection:bg-primary/30"
      >
        <div
          v-if="filteredLogs.length === 0"
          class="flex h-full flex-col items-center justify-center space-y-2 text-status-neutral opacity-50"
        >
          <Terminal class="size-8" />
          <p>{{ t('admin.logs.waiting') }}</p>
        </div>

        <div
          v-for="(entry, index) in filteredLogs"
          :key="index"
          class="flex gap-4 rounded-sm px-2 py-0.5 transition-colors duration-[var(--motion-fast)] ease-[var(--ease-out-quint)] hover:bg-sidebar-foreground/5"
        >
          <span class="w-20 shrink-0 select-none text-status-neutral">{{ formatTime(entry.timestamp) }}</span>
          <span class="w-24 shrink-0 truncate text-status-neutral italic opacity-60 transition-opacity">[{{ entry.source }}]</span>
          <span class="break-all whitespace-pre-wrap leading-relaxed" :class="levelColor(entry.level)">
            {{ entry.message }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>
