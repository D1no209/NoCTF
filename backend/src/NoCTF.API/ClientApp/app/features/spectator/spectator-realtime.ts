import * as signalR from '@microsoft/signalr'
import { startRealtimeWithRetry } from '../../lib/realtime-retry'

/** Never share a connection, bearer token or staff group with the workbench. */
export function watchSpectator(competitionId: string, invalidate: () => void) {
  const abort = new AbortController()
  const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/v1/spectators', { withCredentials: false,
      ...(import.meta.dev ? { transport: signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling } : {}),
    }).withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build()
  let starting = false
  const join = async () => { await hub.invoke('JoinSpectator', competitionId); if (!abort.signal.aborted) invalidate() }
  const start = async () => {
    if (starting || abort.signal.aborted) return
    starting = true
    try {
      const ready = await startRealtimeWithRetry(async () => {
        if (hub.state === signalR.HubConnectionState.Disconnected) await hub.start()
        await join()
      }, abort.signal, () => !abort.signal.aborted)
      if (!ready) await hub.stop()
    } finally { starting = false }
  }
  hub.on('spectatorChanged', (id: string) => { if (id.replaceAll('-', '').toLowerCase() === competitionId.replaceAll('-', '').toLowerCase()) invalidate() })
  hub.onreconnected(() => { void start() })
  hub.onclose(() => { if (!abort.signal.aborted) void start() })
  void start()
  return () => { abort.abort(); void hub.stop() }
}
