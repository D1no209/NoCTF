import type { HubConnection, HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { startRealtimeWithRetry } from './realtime-retry'

/** The subset of `@microsoft/signalr` the shared hub plumbing needs. */
export interface SignalRModule {
  HubConnectionBuilder: typeof HubConnectionBuilder
  HttpTransportType: typeof HttpTransportType
  LogLevel: typeof LogLevel
}

export interface HubConnectionOptions {
  signalR: SignalRModule
  url: string
  locale: string
  accessTokenFactory: () => string | Promise<string>
  /**
   * Skip WebSockets: the dev Vite ws proxy resets SignalR and can crash Nuxt,
   * so development connections use SSE/long-polling while production is direct.
   */
  preferLongPolling: boolean
}

export function buildHubConnection(options: HubConnectionOptions): HubConnection {
  return new options.signalR.HubConnectionBuilder()
    .withUrl(options.url, {
      headers: { 'Accept-Language': options.locale },
      accessTokenFactory: options.accessTokenFactory,
      ...(options.preferLongPolling
        ? { transport: options.signalR.HttpTransportType.ServerSentEvents | options.signalR.HttpTransportType.LongPolling }
        : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(options.signalR.LogLevel.Warning)
    .build()
}

export interface HubStartGate<TConnection> {
  /** The connection owned by the caller, or null before it is created. */
  current: () => TConnection | null
  isConnected: (connection: TConnection) => boolean
  isDisconnected: (connection: TConnection) => boolean
  start: (connection: TConnection) => Promise<void>
}

export interface HubStartOptions<TConnection> {
  /** False once no subscriber needs the connection any more. */
  isWanted: (connection: TConnection) => boolean
  /** Runs once a (re)started connection is ready, e.g. rejoin groups or reread REST. */
  onStarted: (connection: TConnection) => Promise<void> | void
}

export interface HubStarter {
  /** Starts the current connection once, retrying with backoff while it is wanted. */
  ensureStarted: () => Promise<void>
  /** Cancels an in-flight start and forgets it, for teardown. */
  abortStart: () => void
}

/**
 * Shared initial-connection state machine for the realtime hubs: one in-flight
 * attempt, retries with backoff while the connection is still wanted, and
 * cancellation on teardown. Group joins and business invalidation stay with the
 * owning hub.
 */
export function createHubStarter<TConnection>(
  gate: HubStartGate<TConnection>,
  options: HubStartOptions<TConnection>,
): HubStarter {
  let startPromise: Promise<void> | null = null
  let startAbortController: AbortController | null = null

  async function ensureStarted(): Promise<void> {
    const connection = gate.current()
    if (!connection || gate.isConnected(connection)) return
    if (startPromise) return startPromise
    if (!gate.isDisconnected(connection)) return

    const abortController = new AbortController()
    startAbortController = abortController
    const task = (async () => {
      const started = await startRealtimeWithRetry(
        async () => {
          if (gate.isDisconnected(connection)) await gate.start(connection)
        },
        abortController.signal,
        () => gate.current() === connection && options.isWanted(connection),
      )
      if (started && gate.current() === connection) await options.onStarted(connection)
    })()
    startPromise = task.finally(() => {
      if (startAbortController !== abortController) return
      startAbortController = null
      startPromise = null
    })
    await startPromise
  }

  function abortStart(): void {
    startAbortController?.abort()
    startAbortController = null
    startPromise = null
  }

  return { ensureStarted, abortStart }
}

/** Stop a hub connection without surfacing teardown failures. */
export async function stopHubConnection(connection: HubConnection | null): Promise<void> {
  if (!connection) return
  await connection.stop().catch(() => undefined)
}
