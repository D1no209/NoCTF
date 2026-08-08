import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse } from '~/api'
import { getAccessToken } from '~/lib/session'

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

  async function start(): Promise<void> {
    if (connection) return
    state.value = 'connecting'
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/v1/admin/platform-logs', {
        accessTokenFactory: () => getAccessToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection = hub
    hub.on('platformLogReceived', onLog)
    hub.onreconnecting(() => {
      state.value = 'reconnecting'
    })
    hub.onreconnected(() => {
      state.value = 'connected'
    })
    hub.onclose(() => {
      state.value = 'disconnected'
    })
    try {
      await hub.start()
      state.value = 'connected'
    }
    catch {
      connection = null
      state.value = 'disconnected'
    }
  }

  async function stop(): Promise<void> {
    const current = connection
    connection = null
    if (current) {
      try {
        await current.stop()
      }
      catch {
        // Ignore teardown failures.
      }
    }
    state.value = 'disconnected'
  }

  onScopeDispose(() => {
    void stop()
  })

  return { state, start, stop }
}
