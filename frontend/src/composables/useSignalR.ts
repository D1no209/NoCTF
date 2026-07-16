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
    try {
      await conn.start()
      isConnected.value = true
      error.value = null
      options.onConnected?.()
    } catch (err) {
      isConnected.value = false
      error.value = err instanceof Error ? err : new Error(String(err))
      options.onDisconnected?.(error.value)
    }
  }

  async function stop() {
    try {
      await conn.stop()
    } finally {
      isConnected.value = false
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

  function onKohUpdate(callback: (dto: { challengeId: string; controllerTeamId: string | null; timestamp: string }) => void) {
    conn.on('ReceiveKohUpdate', (dto: { challengeId: string; controllerTeamId: string | null; timestamp: string }) => callback(dto))
  }

  return { connection, isConnected, error, start, stop, onRoundStarted, onAttackLog, onKohUpdate }
}
