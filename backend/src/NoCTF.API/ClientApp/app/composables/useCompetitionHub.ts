import * as signalR from '@microsoft/signalr'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
} from '~/api'
import { getAccessToken, getRealtimeAccessToken } from '~/lib/session'
import { startRealtimeWithRetry } from '~/lib/realtime-retry'

/**
 * 竞赛实时 hub(/hubs/v1/competitions)。
 *
 * 事件载荷只带 id/摘要,UI 收到后应重新 GET 对应资源。
 * 连接为模块级单例:所有 watchCompetition 订阅复用同一条连接,
 * 最后一个订阅者离开时断连。未登录(无 access token)时不连接。
 */
export interface ScoreboardUpdatedNotification {
  competitionId: string
  version: string
  schemaRevision: string
  challengeCatalogRevision: string
}

export interface CompetitionLifecycleChangedNotification {
  competitionId: string
  from: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
  to: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
  occurredAt: string
}

export interface CompetitionEventChangedNotification {
  competitionId: string
  eventId: string
  kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol
  level: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol
  occurredAt: string
}

export type GameplayFactStateChangedNotification = Required<
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse
>

export interface CompetitionHubClientEvents {
  scoreboardUpdated: ScoreboardUpdatedNotification
  competitionLifecycleChanged: CompetitionLifecycleChangedNotification
  competitionEventChanged: CompetitionEventChangedNotification
  gameplayFactStateChanged: GameplayFactStateChangedNotification
}

export type CompetitionHubHandlers = {
  [TEvent in keyof CompetitionHubClientEvents]?: (
    payload: CompetitionHubClientEvents[TEvent]
  ) => void
} & {
  /** 连接(重)建立并完成 Join 后触发,用于全量刷新。 */
  onReconnected?: () => void
}

interface Subscriber {
  competitionId: string
  handlers: CompetitionHubHandlers
}

const GROUP_EVENTS = [
  'scoreboardUpdated',
  'competitionLifecycleChanged',
  'competitionEventChanged',
] as const

const HEARTBEAT_MS = 45_000

let connection: signalR.HubConnection | null = null
let startPromise: Promise<void> | null = null
let startAbortController: AbortController | null = null
const subscribers = new Map<symbol, Subscriber>()
const heartbeats = new Map<string, ReturnType<typeof setInterval>>()
/** 已成功 Join 的竞赛(用于断线重连后恢复)。 */
const joined = new Set<string>()

function normalizeGuid(value: unknown): string {
  return typeof value === 'string' ? value.replaceAll('-', '').toLowerCase() : ''
}

export function competitionHubString(payload: unknown, key: string): string | null {
  if (!payload || typeof payload !== 'object') return null
  const value = Reflect.get(payload, key)
  return typeof value === 'string' ? value : null
}

function dispatch<TEvent extends keyof CompetitionHubClientEvents>(
  event: TEvent,
  payload: CompetitionHubClientEvents[TEvent],
  direct = false,
): void {
  const competitionId = normalizeGuid(competitionHubString(payload, 'competitionId'))
  for (const subscriber of subscribers.values()) {
    const handler = subscriber.handlers[event] as
      | ((notification: CompetitionHubClientEvents[TEvent]) => void)
      | undefined
    if (!handler) continue
    if (!direct && competitionId && normalizeGuid(subscriber.competitionId) !== competitionId) continue
    handler(payload)
  }
}

function ensureConnection(): signalR.HubConnection {
  if (connection) return connection
  const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/v1/competitions', {
      accessTokenFactory: getRealtimeAccessToken,
      // 开发环境经 Vite ws 代理转发 SignalR WebSocket 会被重置并拖垮 Nuxt 进程,
      // dev 下跳过 WebSockets 走 SSE/长轮询;生产直连后端,不受影响。
      ...(import.meta.dev
        ? { transport: signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling }
        : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  for (const event of GROUP_EVENTS) {
    hub.on(event, (payload: CompetitionHubClientEvents[typeof event]) => dispatch(event, payload))
  }
  hub.on(
    'gameplayFactStateChanged',
    (payload: GameplayFactStateChangedNotification) =>
      dispatch('gameplayFactStateChanged', payload, true),
  )

  hub.onreconnected(() => {
    if (connection === hub) void rejoinAll()
  })
  hub.onclose(() => {
    if (connection !== hub) return
    joined.clear()
    stopAllHeartbeats()
    if (subscribers.size > 0) void ensureStarted()
  })
  connection = hub
  return hub
}

function stopAllHeartbeats(): void {
  for (const timer of heartbeats.values()) clearInterval(timer)
  heartbeats.clear()
}

function startHeartbeat(competitionId: string): void {
  if (heartbeats.has(competitionId)) return
  heartbeats.set(
    competitionId,
    setInterval(() => {
      const hub = connection
      if (!hub || hub.state !== signalR.HubConnectionState.Connected) return
      hub.invoke('HeartbeatCompetition', competitionId).catch(() => undefined)
    }, HEARTBEAT_MS),
  )
}

async function join(competitionId: string): Promise<void> {
  const hub = connection
  if (!hub) return
  if (hub.state !== signalR.HubConnectionState.Connected) return
  if (joined.has(competitionId)) return
  await hub.invoke('JoinCompetition', competitionId)
  joined.add(competitionId)
  startHeartbeat(competitionId)
}

async function rejoinAll(): Promise<void> {
  const ids = new Set([...subscribers.values()].map((s) => s.competitionId))
  joined.clear()
  for (const id of ids) {
    try {
      await join(id)
    }
    catch {
      // 单个竞赛重新 Join 失败不影响其他订阅。
    }
  }
  for (const subscriber of subscribers.values()) subscriber.handlers.onReconnected?.()
}

async function ensureStarted(): Promise<void> {
  // 首次匿名访问不建立 hub；已认证连接丢失 token 时仍让
  // accessTokenFactory 使用 refresh cookie 恢复连接。
  if (!getAccessToken() && !connection) return
  const hub = ensureConnection()
  if (hub.state === signalR.HubConnectionState.Connected) {
    await rejoinAll()
    return
  }
  if (startPromise) return startPromise
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
    if (started && connection === hub) await rejoinAll()
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
  joined.clear()
  stopAllHeartbeats()
  await hub.stop().catch(() => undefined)
}

/**
 * 订阅某竞赛的实时事件。返回 unsubscribe(清理心跳与事件,
 * 最后一个订阅者离开时断开连接)。未登录时不连接,登录后由页面重新订阅。
 */
export function watchCompetition(
  competitionId: string,
  handlers: CompetitionHubHandlers,
): () => void {
  const key = Symbol('competition-watch')
  subscribers.set(key, { competitionId, handlers })
  void ensureStarted()
    .then(() => subscribers.has(key) ? join(competitionId) : undefined)
    .catch(() => undefined)

  return () => {
    subscribers.delete(key)
    const stillWatched = [...subscribers.values()].some((s) => s.competitionId === competitionId)
    if (!stillWatched) {
      const timer = heartbeats.get(competitionId)
      if (timer) clearInterval(timer)
      heartbeats.delete(competitionId)
      joined.delete(competitionId)
    }
    void teardownIfIdle()
  }
}
