import { currentLocale } from '../utils/i18n'
import * as signalR from '@microsoft/signalr'
import { getAccessToken, getRealtimeAccessToken } from '../lib/session'
import { buildHubConnection, createHubStarter, stopHubConnection } from '../lib/signalr-hub'

export interface NotificationHubHandlers {
  navigationCapabilitiesChanged?: () => void
  theoryChanged?: (activityId: string) => void
  notificationChanged?: () => void
  onReconnected?: () => void
}

let connection: signalR.HubConnection | null = null
const subscribers = new Map<symbol, NotificationHubHandlers>()

function notifyChanged(): void {
  for (const handlers of subscribers.values()) handlers.notificationChanged?.()
}

function notifyReconnected(): void {
  for (const handlers of subscribers.values()) handlers.onReconnected?.()
}

function notifyTheoryChanged(activityId: string): void {
  for (const handlers of subscribers.values()) handlers.theoryChanged?.(activityId)
}

function ensureConnection(): signalR.HubConnection {
  if (connection) return connection
  const hub = buildHubConnection({
    signalR,
    url: '/hubs/v1/notifications',
    locale: currentLocale(),
    accessTokenFactory: getRealtimeAccessToken,
    preferLongPolling: import.meta.dev,
  })
  hub.on('notificationChanged', notifyChanged)
  hub.on('navigationCapabilitiesChanged', () => {
    for (const handlers of subscribers.values()) handlers.navigationCapabilitiesChanged?.()
  })
  hub.on('theoryChanged', notifyTheoryChanged)
  hub.onreconnected(() => {
    if (connection === hub) notifyReconnected()
  })
  hub.onclose(() => {
    if (connection === hub && subscribers.size > 0) void ensureStarted()
  })
  connection = hub
  return hub
}

const starter = createHubStarter({
  current: () => connection,
  isConnected: hub => hub.state === signalR.HubConnectionState.Connected,
  isDisconnected: hub => hub.state === signalR.HubConnectionState.Disconnected,
  start: async hub => {
    if (hub.state === signalR.HubConnectionState.Disconnected) await hub.start()
  },
}, {
  isWanted: () => subscribers.size > 0,
  onStarted: () => notifyReconnected(),
})

async function ensureStarted(): Promise<void> {
  // 首次匿名访问不建立 hub；已认证连接丢失 token 时仍让
  // accessTokenFactory 使用 refresh cookie 恢复连接。
  if (!getAccessToken() && !connection) return
  ensureConnection()
  await starter.ensureStarted()
}

async function teardownIfIdle(): Promise<void> {
  if (subscribers.size > 0 || !connection) return
  const hub = connection
  starter.abortStart()
  connection = null
  await stopHubConnection(hub)
}

/** A single authenticated connection is shared by shell and notification page. */
export function watchNotifications(handlers: NotificationHubHandlers): () => void {
  const key = Symbol('notification-watch')
  subscribers.set(key, handlers)
  void ensureStarted().catch(() => undefined)
  return () => {
    subscribers.delete(key)
    void teardownIfIdle()
  }
}
