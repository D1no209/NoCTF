<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { adminApi } from '@/api/noctf'
import { Button } from '@/components/ui/button'
import { 
  Terminal, 
  Play, 
  Pause, 
  Trash2, 
  Activity, 
  Search,
  ArrowDown
} from 'lucide-vue-next'
import { Input } from '@/components/ui/input'
import { toast } from 'vue-sonner'

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
const levelFilter = ref<'All' | 'Information' | 'Warning' | 'Error'>('All')
const searchTerm = ref('')
const logContainer = ref<HTMLElement | null>(null)

const filteredLogs = computed(() => {
  let result = logs.value
  if (levelFilter.value !== 'All') {
    result = result.filter(l => l.level === levelFilter.value)
  }
  if (searchTerm.value) {
    const s = searchTerm.value.toLowerCase()
    result = result.filter(l => l.message.toLowerCase().includes(s) || l.source.toLowerCase().includes(s))
  }
  return result
})

function levelColor(level: string) {
  if (level === 'Warning') return 'text-amber-400'
  if (level === 'Error') return 'text-rose-400'
  if (level === 'Information') return 'text-emerald-400'
  return 'text-zinc-400'
}

function formatTime(ts: string) {
  const d = new Date(ts)
  return d.toLocaleTimeString('en-GB', { hour12: false }) + '.' + d.getMilliseconds().toString().padStart(3, '0')
}

async function scrollToBottom(force = false) {
  if (paused.value && !force) return
  await nextTick()
  if (logContainer.value) {
    logContainer.value.scrollTop = logContainer.value.scrollHeight
  }
}

watch(filteredLogs, () => scrollToBottom())

const signalR = useSignalR({
  hubUrl: `/hubs/monitor?competitionId=${SYSTEM_COMPETITION_ID}`,
  accessToken: () => auth.accessToken,
})

signalR.connection.value?.on('ReceiveLogEntry', (entry: LogEntryDto) => {
  if (paused.value) return
  logs.value.push(entry)
  if (logs.value.length > 1500) logs.value.splice(0, logs.value.length - 1500)
})

async function fetchHistorical() {
  try {
    const history = await adminApi.logs<LogEntryDto[]>()
    logs.value = [...history, ...logs.value].slice(-1500)
  } catch {
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
  <div class="flex h-[calc(100vh-8rem)] flex-col gap-4">
    <!-- Toolbar -->
    <div class="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-card p-4 rounded-xl border shadow-sm">
      <div class="flex items-center gap-4">
        <div class="flex size-10 items-center justify-center rounded-lg bg-zinc-950 text-emerald-500 shadow-inner border border-white/5">
          <Terminal class="size-5" />
        </div>
        <div>
          <h2 class="text-lg font-bold tracking-tight leading-none">{{ t('admin.logs.subtitle') }}</h2>
          <div class="flex items-center gap-2 mt-1.5">
             <div 
              class="size-2 rounded-full animate-pulse" 
              :class="signalR.isConnected.value ? 'bg-emerald-500' : 'bg-zinc-600'"
            />
            <span class="text-[10px] font-bold uppercase tracking-widest text-muted-foreground">
              {{ signalR.isConnected.value ? t('admin.logs.streaming') : t('common.disconnected') }}
            </span>
          </div>
        </div>
      </div>

      <div class="flex flex-wrap items-center gap-2">
        <div class="relative w-48">
          <Search class="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input v-model="searchTerm" :placeholder="t('admin.logs.filterPlaceholder')" class="h-8 pl-8 text-xs bg-muted/50 border-none" />
        </div>

        <div class="h-4 w-px bg-border mx-1" />

        <div class="flex bg-muted/50 p-1 rounded-md border border-white/5">
          <button
            v-for="lvl in ['All', 'Information', 'Warning', 'Error']"
            :key="lvl"
            class="px-2.5 py-1 text-[10px] font-bold uppercase tracking-tighter rounded transition-all"
            :class="levelFilter === lvl ? 'bg-background shadow-sm text-foreground' : 'text-muted-foreground hover:text-foreground'"
            @click="levelFilter = lvl as any"
          >
            {{ lvl }}
          </button>
        </div>

        <div class="h-4 w-px bg-border mx-1" />

        <Button
          size="sm"
          variant="outline"
          class="h-8 gap-2 border-dashed"
          :class="paused ? 'text-rose-500 border-rose-500/50 hover:bg-rose-500/10' : ''"
          @click="paused = !paused"
        >
          <component :is="paused ? Play : Pause" class="size-3.5" />
          <span class="text-[10px] font-bold uppercase">{{ paused ? t('admin.logs.resume') : t('admin.logs.pause') }}</span>
        </Button>

        <Button size="sm" variant="ghost" class="h-8 size-8 p-0 text-muted-foreground hover:text-destructive" @click="clearLogs">
          <Trash2 class="size-4" />
        </Button>
      </div>
    </div>

    <!-- Terminal Window -->
    <div class="flex-1 min-h-0 rounded-xl border bg-zinc-950 shadow-2xl overflow-hidden flex flex-col relative group">
      <!-- Window Controls Style Header -->
      <div class="h-8 bg-zinc-900 border-b border-white/5 flex items-center px-4 justify-between shrink-0">
        <div class="flex items-center gap-1.5">
          <div class="size-2.5 rounded-full bg-rose-500/20 border border-rose-500/40" />
          <div class="size-2.5 rounded-full bg-amber-500/20 border border-amber-500/40" />
          <div class="size-2.5 rounded-full bg-emerald-500/20 border border-emerald-500/40" />
          <span class="ml-2 text-[10px] font-mono text-zinc-500 flex items-center gap-1.5">
            <Activity class="size-3" />
            stdout.log - {{ filteredLogs.length }} lines
          </span>
        </div>
        <button 
          v-if="paused" 
          @click="scrollToBottom(true)"
          class="text-[10px] font-bold text-emerald-500 flex items-center gap-1 hover:underline"
        >
          <ArrowDown class="size-3" /> {{ t('admin.logs.jumpToEnd') }}
        </button>
      </div>

      <div
        ref="logContainer"
        class="flex-1 overflow-y-auto p-4 font-mono text-[11px] selection:bg-emerald-500/30 custom-scrollbar"
      >
        <div v-if="filteredLogs.length === 0" class="h-full flex flex-col items-center justify-center text-zinc-600 space-y-2 opacity-50">
          <Terminal class="size-8" />
          <p>{{ t('admin.logs.waiting') }}</p>
        </div>
        
        <div
          v-for="(entry, i) in filteredLogs"
          :key="i"
          class="flex gap-4 group/line py-0.5 border-l-2 border-transparent hover:border-white/10 hover:bg-white/[0.02]"
        >
          <span class="text-zinc-600 shrink-0 select-none w-20">{{ formatTime(entry.timestamp) }}</span>
          <span class="shrink-0 w-24 truncate text-zinc-500 italic opacity-60 group-hover/line:opacity-100 transition-opacity">[{{ entry.source }}]</span>
          <span class="break-all whitespace-pre-wrap leading-relaxed" :class="levelColor(entry.level)">
            {{ entry.message }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.custom-scrollbar::-webkit-scrollbar {
  width: 8px;
}
.custom-scrollbar::-webkit-scrollbar-track {
  background: transparent;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.05);
  border-radius: 10px;
}
.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.1);
}
</style>
