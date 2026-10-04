import { currentLocale } from '../utils/i18n'
import { HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import type { NoCTFAPIEndpointsAdministrationPlatformPlatformLogResponse } from '../api/models'
import { getRealtimeAccessToken } from '../lib/session'
import { startRealtimeWithRetry } from '../lib/realtime-retry'

export type PlatformLogHubState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/**
 * SignalR live stream of platform logs (/hubs/v1/admin/platform-logs).
 * Emits the same PlatformLogResponse payload as the REST list endpoint.
 * Disconnects automatically when the owning scope (page) is disposed.
 */
export function usePlatformLogHub(
  onLog: (log: NoCTFAPIEndpointsAdministrationPlatformPlatformLogResponse) => void,
) {
  const state = ref<PlatformLogHubState>('disconnected')
  let connection: HubConnection | null = null
  let startPromise: Promise<void> | null = null
  let startAbortController: AbortController | null = null

  function createConnection(): HubConnection {
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/v1/admin/platform-logs', {
        headers: { 'Accept-Language': currentLocale() },
        accessTokenFactory: getRealtimeAccessToken,
        // 与 useCompetitionHub 相同:dev 下绕过 Vite ws 代理的 WebSocket 崩溃问题。
        ...(import.meta.dev
          ? { transport: HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling }
          : {}),
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection = hub
    hub.on('platformLogReceived', onLog)
    hub.onreconnecting(() => {
      if (connection === hub) state.value = 'reconnecting'
    })
    hub.onreconnected(() => {
      if (connection === hub) state.value = 'connected'
    })
    hub.onclose(() => {
      if (connection !== hub) return
      state.value = 'disconnected'
      void start()
    })
    return hub
  }

  async function start(): Promise<void> {
    const hub = connection ?? createConnection()
    connection = hub
    if (hub.state === HubConnectionState.Connected) {
      state.value = 'connected'
      return
    }
    if (startPromise) return startPromise
    if (hub.state !== HubConnectionState.Disconnected) return

    const abortController = new AbortController()
    startAbortController = abortController
    const task = (async () => {
      const started = await startRealtimeWithRetry(
        async () => {
          state.value = 'connecting'
          if (hub.state === HubConnectionState.Disconnected) await hub.start()
        },
        abortController.signal,
        () => connection === hub,
      )
      if (started && connection === hub) state.value = 'connected'
    })()
    startPromise = task.finally(() => {
      if (startAbortController !== abortController) return
      startAbortController = null
      startPromise = null
    })
    await startPromise
  }

  async function stop(): Promise<void> {
    const current = connection
    startAbortController?.abort()
    startAbortController = null
    connection = null
    startPromise = null
    if (current) {
      try {
        await current.stop()
      }
      catch {
        // Ignore teardown failures.
      }
    }
    if (!connection) state.value = 'disconnected'
  }

  onScopeDispose(() => {
    void stop()
  })

  return { state, start, stop }
}
