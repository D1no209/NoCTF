import { onMounted, ref } from 'vue'
import { adminApi } from '@/api/noctf'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'

export interface LogEntryDto {
  level: string
  message: string
  source: string
  timestamp: string
}

export const SYSTEM_COMPETITION_ID = '00000000-0000-0000-0000-000000000001'

const MAX_LOG_ENTRIES = 1500

export function useAdminLogsPage() {
  const auth = useAuthStore()
  const logs = ref<LogEntryDto[]>([])
  const paused = ref(false)
  const historyError = ref<unknown>(null)

  const signalR = useSignalR({
    hubUrl: `/hubs/monitor?competitionId=${SYSTEM_COMPETITION_ID}`,
    accessToken: () => auth.accessToken,
  })

  signalR.connection.value?.on('ReceiveLogEntry', (entry: LogEntryDto) => {
    if (paused.value)
      return
    logs.value.push(entry)
    if (logs.value.length > MAX_LOG_ENTRIES)
      logs.value.splice(0, logs.value.length - MAX_LOG_ENTRIES)
  })

  async function fetchHistorical() {
    try {
      const history = await adminApi.logs<LogEntryDto[]>()
      logs.value = [...history, ...logs.value].slice(-MAX_LOG_ENTRIES)
    }
    catch (error) {
      historyError.value = error
    }
  }

  function clearLogs() {
    logs.value = []
  }

  function togglePause() {
    paused.value = !paused.value
  }

  onMounted(async () => {
    await fetchHistorical()
    await signalR.start()
  })

  return {
    logs,
    paused,
    historyError,
    signalR,
    fetchHistorical,
    clearLogs,
    togglePause,
  }
}
