import { describe, expect, test } from 'bun:test'

describe('notification realtime invalidation', () => {
  test('shares one authenticated SignalR connection and reconciles after reconnect', async () => {
    const hub = await Bun.file(new URL('../app/composables/useNotificationHub.ts', import.meta.url)).text()
    const shell = await Bun.file(new URL('../app/features/shell/useDefaultLayout.ts', import.meta.url)).text()

    expect(hub).toContain(".withUrl('/hubs/v1/notifications'")
    expect(hub).toContain('accessTokenFactory: getRealtimeAccessToken')
    expect(hub).toContain("hub.on('notificationChanged', notifyChanged)")
    expect(hub).toContain('.withAutomaticReconnect()')
    expect(shell).toContain('watchNotifications({')
    expect(shell).toContain('notificationChanged: () => void refreshOnSignal()')
    expect(shell).toContain('onReconnected: () => void refreshOnSignal()')
    expect(shell).not.toContain('20_000')
    expect(shell).toContain('300_000')
    expect(shell).toContain("document.addEventListener('visibilitychange', refreshWhenVisible)")
  })

  test('refreshes an open inbox and detail without replacing its current rows on failure', async () => {
    const center = await Bun.file(new URL('../app/features/notifications/useNotificationCenter.ts', import.meta.url)).text()

    expect(center).toContain('reset({ preserveItems: true })')
    expect(center).toContain('notificationChanged: () => void refreshInbox()')
    expect(center).toContain('onReconnected: () => void refreshInbox()')
    expect(center).toContain('if (!error.value) markAllRead(items.value[0]?.id)')
    expect(center).toContain('await refreshSelectedThread()')
  })
})
