import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'

const feature = readFileSync(new URL(
  '../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdWebhooksPage.ts',
  import.meta.url,
), 'utf8')
const view = readFileSync(new URL(
  '../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdWebhooksPageView.vue',
  import.meta.url,
), 'utf8')

describe('competition webhook administration', () => {
  test('uses generated APIs for pagination, mutation, rotation and asynchronous tests', () => {
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.get\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.post\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.byTargetId\([^)]*\)\.put\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.byTargetId\([^)]*\)\.delete\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.byTargetId\([^)]*\)\.rotateSecret\.post\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.byTargetId\([^)]*\)\.testDeliveries\.post\(/)
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhooks\.byTargetId\([^)]*\)\.testDeliveries\.byDeliveryId\([^)]*\)\.get\(/)
    expect(feature).toContain('useOffsetPagination')
    expect(view).toContain('<OffsetPagination')
  })

  test('shows the one-time secret and delivery semantics without provider-specific language', () => {
    expect(view).toContain("$t('webhook.secretDescription')")
    expect(view).toContain("$t('webhook.deliveryModelDescription')")
    expect(view).not.toMatch(/QQ|Milky/i)
  })

  test('shows paged delivery diagnostics without rendering protected payloads or secrets', () => {
    expect(feature).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.webhookDeliveries\.get\(/)
    expect(view).toContain("$t('webhook.diagnosticsTitle')")
    expect(view).toContain('delivery.queueAgeSeconds')
    expect(view).toContain('delivery.projectionWaitSeconds')
    expect(view).toContain('delivery.lastHttpStatusCode')
    expect(view).toContain('delivery.deadLetterReason')
    expect(view).not.toContain('delivery.body')
    expect(view).not.toContain('delivery.signingSecret')
  })
})
