import { ref, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import { apiUrl } from '@/api/noctf'

export interface AttackLogDto {
  attackerTeamId: string
  attackerTeamName: string
  victimTeamId: string
  victimTeamName: string
  challengeId: string
  challengeName: string
  roundNumber: number
  timestamp: string
}

export interface UseSignalROptions {
  hubUrl: string
  accessToken?: () => string | null
  automaticReconnect?: boolean
  onConnected?: () => void
  onDisconnected?: (err?: Error) => void
  onReconnected?: () => void
}

function resolveHubUrl(hubUrl: string) {
  return apiUrl(hubUrl)
}

export function useSignalR(options: UseSignalROptions) {
  const connection = ref<signalR.HubConnection | null>(null)
  const isConnected = ref(false)
  const error = ref<Error | null>(null)
  let disposed = false
  let stopping = false

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
    if (err) error.value = err instanceof Error ? err : new Error(String(err))
    if (disposed || stopping) return
    options.onDisconnected?.(err instanceof Error ? err : err ? new Error(String(err)) : undefined)
  })

  conn.onreconnected(() => {
    isConnected.value = true
    error.value = null
    options.onReconnected?.()
  })

  conn.onreconnecting((err) => {
    isConnected.value = false
    if (err) error.value = err instanceof Error ? err : new Error(String(err))
  })

  async function start() {
    if (disposed) return
    try {
      await conn.start()
      if (disposed) {
        await conn.stop()
        return
      }
      isConnected.value = true
      error.value = null
      options.onConnected?.()
    } catch (err) {
      isConnected.value = false
      error.value = err instanceof Error ? err : new Error(String(err))
      if (!disposed) options.onDisconnected?.(error.value)
    }
  }

  async function stop() {
    disposed = true
    stopping = true
    try {
      await conn.stop()
    } finally {
      isConnected.value = false
      stopping = false
    }
  }

  onUnmounted(() => {
    stop()
  })

  function onRoundStarted(callback: (roundNumber: number) => void) {
    conn.on('ReceiveRoundStarted', (dto: { roundNumber: number }) => callback(dto.roundNumber))
  }

  function onAttackLog(callback: (log: AttackLogDto) => void) {
    conn.on('ReceiveAttackLog', (log: AttackLogDto) => callback(log))
  }

  function onKohUpdate(callback: (dto: { challengeId: string; controllerTeamId: string | null; controllerTeamName: string | null; timestamp: string }) => void) {
    conn.on('ReceiveKohUpdate', (dto: any) => callback(dto))
  }

  return { connection, isConnected, error, start, stop, onRoundStarted, onAttackLog, onKohUpdate }
}
