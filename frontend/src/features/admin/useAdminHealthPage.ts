import { onMounted, onUnmounted, ref } from 'vue'
import { adminApi } from '@/api/noctf'

export interface AdminHealthCheckDto {
  name: string
  status: string
  description?: string
}

export interface AdminHealthResponseDto {
  status: string
  checks: AdminHealthCheckDto[]
}

// Manual polling (not vue-query), matching V1: fetch on mount, silent refresh
// every 15s, and record every failure in lastError so themes can decide how
// to present it (V1 toasts on every failure; V2 only surfaces manual ones).
export function useAdminHealthPage() {
  const health = ref<AdminHealthResponseDto | null>(null)
  const loading = ref(false)
  const lastUpdated = ref<Date | null>(null)
  const lastError = ref<unknown>(null)
  let intervalId: ReturnType<typeof setInterval> | null = null

  async function fetchHealth(silent = false) {
    if (!silent)
      loading.value = true
    try {
      health.value = await adminApi.health<AdminHealthResponseDto>()
      lastUpdated.value = new Date()
      lastError.value = null
    }
    catch (error) {
      health.value = null
      lastError.value = error
    }
    finally {
      if (!silent)
        loading.value = false
    }
  }

  onMounted(() => {
    void fetchHealth()
    intervalId = setInterval(() => void fetchHealth(true), 15_000)
  })

  onUnmounted(() => {
    if (intervalId)
      clearInterval(intervalId)
  })

  return {
    health,
    loading,
    lastUpdated,
    lastError,
    fetchHealth,
  }
}
