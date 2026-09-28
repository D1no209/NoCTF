import * as signalR from '@microsoft/signalr'
import { getAccessToken, getRealtimeAccessToken } from '../lib/session'
import { startRealtimeWithRetry } from '../lib/realtime-retry'

export interface NotificationHubHandlers {
  notificationChanged?: () => void
  onReconnected?: () => void
}

let connection: signalR.HubConnection | null = null
let startPromise: Promise<void> | null = null
let startAbortController: AbortController | null = null
const subscribers = new Map<symbol, NotificationHubHandlers>()

function notifyChanged(): void {
  for (const handlers of subscribers.values()) handlers.notificationChanged?.()
}

function notifyReconnected(): void {
  for (const handlers of subscribers.values()) handlers.onReconnected?.()
}

function ensureConnection(): signalR.HubConnection {
  if (connection) return connection
  const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/v1/notifications', {
      accessTokenFactory: getRealtimeAccessToken,
      ...(import.meta.dev
        ? { transport: signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling }
        : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  hub.on('notificationChanged', notifyChanged)
  hub.onreconnected(() => {
    if (connection === hub) notifyReconnected()
  })
  hub.onclose(() => {
    if (connection === hub && subscribers.size > 0) void ensureStarted()
  })
  connection = hub
  return hub
}

async function ensureStarted(): Promise<void> {
  if (!getAccessToken() && !connection) return
  const hub = ensureConnection()
  if (hub.state === signalR.HubConnectionState.Connected || startPromise) return startPromise ?? undefined
  if (hub.state !== signalR.HubConnectionState.Disconnected) return

  const abortController = new AbortController()
  startAbortController = abortController
  const task = (async () => {
    const started = await startRealtimeWithRetry(
      async () => {
        if (hub.state === signalR.HubConnectionState.Disconnected) await hub.start()
      },
      abortController.signal,
      () => connection === hub && subscribers.size > 0,
    )
    if (started && connection === hub) notifyReconnected()
  })()
  startPromise = task.finally(() => {
    if (startAbortController !== abortController) return
    startAbortController = null
    startPromise = null
  })
  await startPromise
}

async function teardownIfIdle(): Promise<void> {
  if (subscribers.size > 0 || !connection) return
  const hub = connection
  startAbortController?.abort()
  startAbortController = null
  connection = null
  startPromise = null
  await hub.stop().catch(() => undefined)
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
