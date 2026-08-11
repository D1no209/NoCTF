import { describe, expect, test } from 'bun:test'

describe('platform branding contract', () => {
  test('renders the revisioned logo URL supplied by the API', async () => {
    const source = await Bun.file(
      new URL('../app/pages/admin/platform/index.vue', import.meta.url),
    ).text()

    expect(source).toContain('configuration.value?.logoUrl ?? null')
    expect(source).not.toContain('/api/v1/platform/logo')
    expect(source).not.toContain('logoVersion')
  })
})
