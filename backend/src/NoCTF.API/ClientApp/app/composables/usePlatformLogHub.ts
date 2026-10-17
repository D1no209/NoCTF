import { currentLocale } from '../utils/i18n'
import { HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse } from '../api'
import { getRealtimeAccessToken } from '../lib/session'
import { buildHubConnection, createHubStarter, stopHubConnection } from '../lib/signalr-hub'

export type PlatformLogHubState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/**
 * SignalR live stream of platform logs (/hubs/v1/admin/platform-logs).
 * Emits the same PlatformLogResponse payload as the REST list endpoint.
 * Disconnects automatically when the owning scope (page) is disposed.
 */
export function usePlatformLogHub(
  onLog: (log: NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse) => void,
) {
  const state = ref<PlatformLogHubState>('disconnected')
  let connection: HubConnection | null = null

  function createConnection(): HubConnection {
    const hub = buildHubConnection({
      signalR: { HubConnectionBuilder, HttpTransportType, LogLevel },
      url: '/hubs/v1/admin/platform-logs',
      locale: currentLocale(),
      accessTokenFactory: getRealtimeAccessToken,
      // 与 useCompetitionHub 相同:dev 下绕过 Vite ws 代理的 WebSocket 崩溃问题。
      preferLongPolling: import.meta.dev,
    })
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

  const starter = createHubStarter({
    current: () => connection,
    isConnected: hub => hub.state === HubConnectionState.Connected,
    isDisconnected: hub => hub.state === HubConnectionState.Disconnected,
    start: async hub => {
      state.value = 'connecting'
      if (hub.state === HubConnectionState.Disconnected) await hub.start()
    },
  }, {
    isWanted: hub => connection === hub,
    onStarted: () => { state.value = 'connected' },
  })

  async function start(): Promise<void> {
    connection = connection ?? createConnection()
    if (connection.state === HubConnectionState.Connected) {
      state.value = 'connected'
      return
    }
    await starter.ensureStarted()
  }

  async function stop(): Promise<void> {
    const current = connection
    starter.abortStart()
    connection = null
    await stopHubConnection(current)
    if (!connection) state.value = 'disconnected'
  }

  onScopeDispose(() => {
    void stop()
  })

  return { state, start, stop }
}
