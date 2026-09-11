import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('email administration exposes the persisted human-verification switch', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()
  const types = await sourceFile('app/api/types.gen.ts').text()

  expect(controller).toContain('data.humanVerification')
  expect(controller).toContain('body: { humanVerification: { enabled: humanVerificationEnabled.value } }')
  expect(controller).toContain('await refreshPlatform()')
  expect(view).toContain('v-model="humanVerificationEnabled"')
  expect(view).toContain("$t('ui.humanVerificationProviderDeploymentRequired')")
  expect(types).toContain('PlatformHumanVerificationPatchRequest')
})

test('email administration keeps a retryable page body when configuration loading fails', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()

  expect(controller).toContain('loadError.value = parseApiError(error).message')
  expect(controller).toContain('load,')
  expect(view).toContain('v-else-if="!configuration"')
  expect(view).toContain('@click="load"')
  expect(view).toContain("$t('ui.retry')")
})
