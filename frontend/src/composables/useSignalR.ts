import * as signalR from '@microsoft/signalr'
import { onUnmounted, ref } from 'vue'
import { apiUrl } from '@/api/noctf'

export const COMPETITION_HUB_PATH = '/hubs/v1/competitions'

export interface UseSignalROptions {
  hubUrl: string
  accessToken?: () => string | null
  automaticReconnect?: boolean
  onConnected?: () => void
  onDisconnected?: (err?: Error) => void
  onReconnecting?: (err?: Error) => void
  onReconnected?: () => void
}

function resolveHubUrl(hubUrl: string) {
  return apiUrl(hubUrl)
}

export function useSignalR(options: UseSignalROptions) {
  const connection = ref<signalR.HubConnection | null>(null)
  const isConnected = ref(false)
  const error = ref<Error | null>(null)

  const builder = new signalR.HubConnectionBuilder()
    .withUrl(resolveHubUrl(options.hubUrl), {
      accessTokenFactory: options.accessToken ? () => options.accessToken!() ?? '' : undefined,
    })

  if (options.automaticReconnect !== false) {
    builder.withAutomaticReconnect()
  }

  const conn = builder.build()
  connection.value = conn

  conn.onclose((err) => {
    isConnected.value = false
    if (err)
      error.value = err instanceof Error ? err : new Error(String(err))
    options.onDisconnected?.(err instanceof Error ? err : err ? new Error(String(err)) : undefined)
  })

  conn.onreconnected(() => {
    isConnected.value = true
    error.value = null
    options.onReconnected?.()
  })

  conn.onreconnecting((err) => {
    isConnected.value = false
    if (err)
      error.value = err instanceof Error ? err : new Error(String(err))
    options.onReconnecting?.(err instanceof Error ? err : err ? new Error(String(err)) : undefined)
  })

  async function start() {
    try {
      await conn.start()
      isConnected.value = true
      error.value = null
      options.onConnected?.()
    }
    catch (err) {
      isConnected.value = false
      error.value = err instanceof Error ? err : new Error(String(err))
      options.onDisconnected?.(error.value)
    }
  }

  async function stop() {
    try {
      await conn.stop()
    }
    finally {
      isConnected.value = false
    }
  }

  onUnmounted(() => {
    stop()
  })

  return { connection, isConnected, error, start, stop }
}
