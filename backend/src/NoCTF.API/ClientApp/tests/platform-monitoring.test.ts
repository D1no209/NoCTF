import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('platform monitoring', () => {
  test('uses the generated administration endpoint without exposing scrape details', async () => {
    const page = await sourceFile('app/pages/admin/platform/monitoring.vue').text()
    const navigation = await sourceFile('app/pages/admin/platform.vue').text()
    const sdk = await sourceFile('app/api/sdk.gen.ts').text()

    expect(navigation).toContain("to: '/admin/platform/monitoring'")
    expect(page).toContain('adminPlatformGetMonitoring()')
    expect(page).toContain('snapshot?.dashboardUrl')
    expect(page).not.toContain('/metrics')
    expect(page).not.toContain('prometheus:9090')
    expect(page).not.toContain('localhost')
    expect(sdk).toContain('export const adminPlatformGetMonitoring')
  })

  test('refreshes only while the page is visible and preserves the last snapshot on failure', async () => {
    const page = await sourceFile('app/pages/admin/platform/monitoring.vue').text()

    expect(page).toContain("document.visibilityState === 'visible'")
    expect(page).toContain('setInterval(refreshWhenVisible, 15_000)')
    expect(page).not.toMatch(/responseError[\s\S]{0,200}snapshot\.value\s*=\s*null/)
  })

  test('separates Prometheus, JetStream, transaction messaging, and idle samples', async () => {
    const page = await sourceFile('app/pages/admin/platform/monitoring.vue').text()
    const types = await sourceFile('app/api/types.gen.ts').text()

    expect(page).toContain("title: \"ui.natsJetstream\"")
    expect(page).toContain("title: \"ui.transactionalMessaging\"")
    expect(page).toContain('METRIC.criticalQueuePendingCount')
    expect(page).toContain('METRIC.criticalQueueAckPendingCount')
    expect(page).toContain('METRIC.criticalQueueRedeliveredCount')
    expect(page).toContain('METRIC.wolverineOutboxCount')
    expect(page).toContain('METRIC.wolverineInboxCount')
    expect(page).toContain('snapshot.prometheusAvailable')
    expect(page).toContain('snapshot.natsAvailable')
    expect(page).toContain('STATUS.noSamples')
    expect(types).toContain('prometheusAvailable?: boolean')
    expect(types).toContain('natsAvailable?: boolean')
  })

  test('renders current snapshot data as dense status, latency, and quota visualizations', async () => {
    const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformMonitoringPageView.vue').text()
    const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformMonitoringPage.ts').text()
    const mock = await sourceFile('mock/api.ts').text()

    expect(view).toContain('data-slot="monitoring-health-orbit"')
    expect(view).toContain('data-slot="monitoring-meter"')
    expect(view).toContain('data-slot="monitoring-latency-meter"')
    expect(view).toContain('v-for="kind in HERO_KINDS"')
    expect(view).toContain('v-for="row in snapshot.poolResources"')
    expect(controller).toContain('const healthPercent = computed')
    expect(controller).toContain('function latencyVisualPercent')
    expect(mock).toContain("route === '/admin/platform/monitoring'")
    expect(mock).toContain('function mockMonitoringSnapshot()')
  })
})
