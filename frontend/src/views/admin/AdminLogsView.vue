<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { adminApi } from '@/api/noctf'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import PageHeader from '@/components/layout/PageHeader.vue'

const { t } = useI18n()

interface LogEntryDto {
  level: string
  message: string
  source: string
  timestamp: string
}

// A fixed "system" GUID used to connect to MonitorHub for log streaming
const SYSTEM_COMPETITION_ID = '00000000-0000-0000-0000-000000000001'

const auth = useAuthStore()
const logs = ref<LogEntryDto[]>([])
const paused = ref(false)
const levelFilter = ref<'All' | 'Information' | 'Warning' | 'Error'>('All')
const logContainer = ref<HTMLElement | null>(null)

const filteredLogs = computed(() => {
  if (levelFilter.value === 'All') return logs.value
  return logs.value.filter(l => l.level === levelFilter.value)
})

function levelClass(level: string) {
  if (level === 'Warning') return 'text-yellow-500'
  if (level === 'Error') return 'text-red-500'
  return 'text-foreground'
}

function levelBadgeVariant(level: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (level === 'Warning') return 'secondary'
  if (level === 'Error') return 'destructive'
  return 'outline'
}

function formatTime(ts: string) {
  return new Date(ts).toLocaleTimeString()
}

async function scrollToBottom() {
  if (paused.value) return
  await nextTick()
  if (logContainer.value) {
    logContainer.value.scrollTop = logContainer.value.scrollHeight
  }
}

watch(filteredLogs, scrollToBottom)

// SignalR connection
const signalR = useSignalR({
  hubUrl: `/hubs/monitor?competitionId=${SYSTEM_COMPETITION_ID}`,
  accessToken: () => auth.accessToken,
  onConnected: () => console.log('[AdminLogs] SignalR connected'),
})

signalR.connection.value?.on('ReceiveLogEntry', (entry: LogEntryDto) => {
  logs.value.push(entry)
  // Keep buffer bounded in the UI (max 1000 entries)
  if (logs.value.length > 1000) logs.value.splice(0, logs.value.length - 1000)
})

async function fetchHistorical() {
  try {
    logs.value = [...await adminApi.logs<LogEntryDto[]>(), ...logs.value]
  } catch {
    // non-fatal
  }
}

onMounted(async () => {
  await fetchHistorical()
  await signalR.start()
  await scrollToBottom()
})
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-4 md:p-6">
    <!-- Header -->
    <PageHeader :title="t('admin.logs.title')">
      <template #actions>
      <div class="flex items-center gap-2 flex-wrap">
        <!-- Level filters -->
        <div class="flex gap-1">
          <Button
            v-for="lvl in ['All', 'Information', 'Warning', 'Error'] as const"
            :key="lvl"
            :variant="levelFilter === lvl ? 'default' : 'outline'"
            size="sm"
            @click="levelFilter = lvl"
          >
            {{ lvl === 'All' ? t('common.all') : lvl === 'Information' ? t('common.information') : lvl === 'Warning' ? t('common.warning') : t('common.error') }}
          </Button>
        </div>
        <!-- Pause toggle -->
        <Button
          :variant="paused ? 'destructive' : 'secondary'"
          size="sm"
          @click="paused = !paused"
        >
          {{ paused ? `▶ ${t('common.resume')}` : `⏸ ${t('common.pause')}` }}
        </Button>
        <!-- Connection status -->
        <Badge :variant="signalR.isConnected.value ? 'default' : 'secondary'">
          {{ signalR.isConnected.value ? t('common.live') : t('common.disconnected') }}
        </Badge>
      </div>
      </template>
    </PageHeader>

    <!-- Log viewer -->
    <div
      ref="logContainer"
      class="flex-1 overflow-y-auto rounded-md border bg-muted/30 p-3 font-mono text-xs space-y-0.5 min-h-0"
      style="max-height: calc(100vh - 160px)"
    >
      <div v-if="filteredLogs.length === 0" class="text-muted-foreground py-4 text-center">
        {{ t('admin.logs.empty') }}
      </div>
      <div
        v-for="(entry, i) in filteredLogs"
        :key="i"
        class="flex gap-2 leading-5"
        :class="levelClass(entry.level)"
      >
        <span class="text-muted-foreground shrink-0">{{ formatTime(entry.timestamp) }}</span>
        <span class="shrink-0 w-20 truncate text-muted-foreground">{{ entry.source }}</span>
        <Badge :variant="levelBadgeVariant(entry.level)" class="shrink-0 text-[10px] px-1 py-0 h-4">
          {{ entry.level.slice(0, 4).toUpperCase() }}
        </Badge>
        <span class="break-all">{{ entry.message }}</span>
      </div>
    </div>

    <p class="text-xs text-muted-foreground">
      {{ t('admin.logs.showingEntries', { count: filteredLogs.length }) }}
    </p>
  </div>
</template>
