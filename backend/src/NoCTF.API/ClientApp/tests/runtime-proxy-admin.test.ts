import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('Runtime WSRX administration', () => {
  test('competition configuration persists the three access modes and capture limit', async () => {
    const [feature, view] = await Promise.all([
      sourceFile(new URL(
        '../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage.ts',
        import.meta.url,
      )).text(),
      sourceFile(new URL(
        '../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdConfigurationPageView.vue',
        import.meta.url,
      )).text(),
    ])

    expect(feature).toContain('runtimeAccessMode: runtimeAccessMode.value')
    expect(feature).toContain('trafficCaptureEnabled: trafficCaptureEnabled.value')
    expect(feature).toContain('trafficCaptureLimitBytes:')
    expect(view).toContain('value="Direct"')
    expect(view).toContain('value="DirectAndWsrx"')
    expect(view).toContain('value="WsrxOnly"')
    expect(view).toContain("runtime.captureDirectBypassWarning")
  })

  test('traffic monitor uses generated list download export and delete operations', async () => {
    const [feature, view, navigation] = await Promise.all([
      sourceFile(new URL(
        '../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdTrafficCapturesPage.ts',
        import.meta.url,
      )).text(),
      sourceFile(new URL(
        '../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdTrafficCapturesPageView.vue',
        import.meta.url,
      )).text(),
      sourceFile(new URL(
        '../app/features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts',
        import.meta.url,
      )).text(),
    ])

    for (const operation of [
      'adminListRuntimeTrafficCaptures',
      'adminDownloadRuntimeTrafficCapture',
      'adminExportRuntimeTrafficCaptures',
      'adminDeleteRuntimeTrafficCapture',
    ]) expect(feature).toContain(operation)
    expect(view).toContain('<OffsetPagination')
    expect(view).toContain('<AlertDialog')
    expect(navigation).toContain('/traffic-captures')
  })
})
