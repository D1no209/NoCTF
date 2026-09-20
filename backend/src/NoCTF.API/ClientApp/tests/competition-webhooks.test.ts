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
    expect(feature).toContain('adminListCompetitionWebhooks')
    expect(feature).toContain('adminCreateCompetitionWebhook')
    expect(feature).toContain('adminUpdateCompetitionWebhook')
    expect(feature).toContain('adminDeleteCompetitionWebhook')
    expect(feature).toContain('adminRotateCompetitionWebhookSecret')
    expect(feature).toContain('adminCreateCompetitionWebhookTestDelivery')
    expect(feature).toContain('adminGetCompetitionWebhookTestDelivery')
    expect(feature).toContain('useOffsetPagination')
    expect(view).toContain('<OffsetPagination')
  })

  test('shows the one-time secret and delivery semantics without provider-specific language', () => {
    expect(view).toContain("$t('webhook.secretDescription')")
    expect(view).toContain("$t('webhook.deliveryModelDescription')")
    expect(view).not.toMatch(/QQ|Milky/i)
  })
})
