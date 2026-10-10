import { computed, onScopeDispose, ref, watch, type Ref } from 'vue'
import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr'
import { getRealtimeAccessToken } from '../../lib/session'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'

interface MatchInvalidation { competitionId: string; matchId: string; occurredAt: string }
interface Dependencies { eligible: Ref<boolean>; createConnection: () => HubConnection }
function connection() {
  return new HubConnectionBuilder().withUrl('/hubs/v1/live-solo', {
    accessTokenFactory: getRealtimeAccessToken,
    ...(import.meta.dev ? { transport: HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling } : {}),
  }).withAutomaticReconnect([0, 2000, 5000, 10000]).configureLogging(LogLevel.None).build()
}
/** Private invalidations only. Delayed program pages never call this controller. */
export function useLiveSoloHub(competitionId: Ref<string>, matchId: Ref<string | null>, staff: Ref<boolean>, refresh: () => Promise<void>, dependencies?: Dependencies) {
  const auth = dependencies ? null : useAuth()
  const eligible = dependencies?.eligible ?? computed(() => !!auth?.user.value?.userId)
  const connected = ref(false), invalidate = createTrailingRefresh(refresh)
  let hub: HubConnection | null = null, disposed = false, revision = 0, heartbeat: ReturnType<typeof setInterval> | undefined
  function clearHeartbeat() { if (heartbeat) clearInterval(heartbeat); heartbeat = undefined }
  async function start() {
    const generation = ++revision, previous = hub; hub = null; connected.value = false; clearHeartbeat()
    if (previous) await previous.stop().catch(() => {})
    if (disposed || generation !== revision || !eligible.value || !competitionId.value || !matchId.value) return
    const competition = competitionId.value, match = matchId.value, audience = staff.value ? 1 : 0
    const candidate = (dependencies?.createConnection ?? connection)(); hub = candidate
    const current = () => !disposed && generation === revision && hub === candidate
    async function join() {
      await candidate.invoke('JoinMatch', competition, match, audience)
      if (!current()) { await candidate.stop(); return }
      connected.value = true; clearHeartbeat()
      heartbeat = setInterval(() => {
        void candidate.invoke('HeartbeatMatch', match).catch(() => { connected.value = false; clearHeartbeat(); void candidate.stop() })
      }, 5000)
      await invalidate()
    }
    candidate.on('matchChanged', (change: MatchInvalidation) => {
      if (current() && connected.value && change?.competitionId?.toLowerCase() === competition.toLowerCase() && change?.matchId?.toLowerCase() === match.toLowerCase()) void invalidate()
    })
    candidate.onreconnecting(() => { if (current()) { connected.value = false; clearHeartbeat() } })
    candidate.onreconnected(() => { if (current()) void join().catch(() => { connected.value = false; clearHeartbeat(); void candidate.stop() }) })
    candidate.onclose(() => { if (current()) { connected.value = false; clearHeartbeat() } })
    try { await candidate.start(); if (current()) await join(); else await candidate.stop() }
    catch { if (current()) { connected.value = false; clearHeartbeat(); await candidate.stop().catch(() => {}) } }
  }
  watch([competitionId, matchId, staff, eligible], () => { void start() }, { immediate: true })
  onScopeDispose(() => { disposed = true; revision++; clearHeartbeat(); const previous = hub; hub = null; connected.value = false; if (previous) void previous.stop() })
  return { connected }
}
