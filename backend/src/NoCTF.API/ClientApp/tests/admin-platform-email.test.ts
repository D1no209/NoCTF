import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('email administration exposes persisted human-verification providers and secrets', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()
  const types = await sourceFile('app/api/models/index.ts').text()

  expect(controller).toContain('data.humanVerification')
  expect(controller).toContain('{ humanVerification: humanVerificationRequest() }')
  expect(controller).toMatch(/api\.api\.v1\.admin\.platform\.humanVerification\.secret\.put\(/)
  expect(controller).toContain('await refreshPlatform()')
  expect(view).toContain('v-model="humanForm.provider"')
  expect(view).toContain('v-model="humanForm.enabled"')
  expect(view).toContain('v-model="humanForm.runtimeEnabled"')
  expect(controller).toContain('runtimeEnabled: humanForm.runtimeEnabled')
  expect(view).toContain('v-model="humanForm.evaluationEnabled"')
  expect(controller).toContain('evaluationEnabled: humanForm.evaluationEnabled')
  expect(view).toContain("humanForm.provider === 'Cap'")
  expect(view).toContain("humanForm.provider === 'Turnstile'")
  expect(view).toContain("$t('administration.label.configureProviderSecret')")
  expect(types).toContain('PlatformHumanVerificationPatchRequest')
  expect(types).toContain('ReplaceHumanVerificationSecretRequest')
})

test('CAP workload is managed through the platform page without exposing credentials', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()
  const types = await sourceFile('app/api/models/index.ts').text()

  expect(controller).toMatch(/api\.api\.v1\.admin\.platform\.humanVerification\.capWorkload\.get\(/)
  expect(controller).toMatch(/api\.api\.v1\.admin\.platform\.humanVerification\.capWorkload\.put\(/)
  expect(controller).toContain('* 16 ** capWorkloadForm.difficulty')
  expect(view).toContain('v-model.number="capWorkloadForm.difficulty"')
  expect(view).toContain('v-model.number="capWorkloadForm.challengeCount"')
  expect(view).toContain('capExpectedHashAttemptsLabel')
  expect(view).toContain('capWorkload.challengeSize ?? 32')
  expect(types).toContain('CapWorkloadConfigurationResponse')
  expect(types).not.toContain('managementApiKey?:')
})

test('email administration keeps a retryable page body when configuration loading fails', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()

  expect(controller).toContain('loadError.value = parseApiError(error).displayMessage')
  expect(controller).toContain('load,')
  expect(view).toContain('v-else-if="!configuration"')
  expect(view).toContain('@click="load"')
  expect(view).toContain("$t('common.label.retry')")
})

test('SMTP test uses only an enabled and fully saved configuration', async () => {
  const controller = await sourceFile('app/features/routes/admin/platform/useAdminPlatformEmailPage.ts').text()
  const view = await sourceFile('app/components/views/page/admin/platform/AdminPlatformEmailPageView.vue').text()

  expect(controller).toContain('const emailDirty = computed(')
  expect(controller).toContain('syncForm(data.emailVerification)')
  expect(view).toContain('!configuration.enabled || emailDirty')
  expect(view).not.toContain('sendingTest || !form.enabled')
})
