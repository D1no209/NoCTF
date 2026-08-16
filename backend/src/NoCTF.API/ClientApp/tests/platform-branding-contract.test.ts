import { describe, expect, test } from 'bun:test'

describe('platform branding contract', () => {
  test('uses the configured platform logo as the browser icon', async () => {
    const source = await Bun.file(
      new URL('../app/app.vue', import.meta.url),
    ).text()

    expect(source).toContain("key: 'platform-icon'")
    expect(source).toContain("rel: 'icon'")
    expect(source).toContain('href: configuration.value.logoUrl')
  })

  test('renders the revisioned logo URL supplied by the API', async () => {
    const source = await Bun.file(
      new URL('../app/pages/admin/platform/index.vue', import.meta.url),
    ).text()

    expect(source).toContain('configuration.value?.logoUrl ?? null')
    expect(source).not.toContain('/api/v1/platform/logo')
    expect(source).not.toContain('logoVersion')
  })
})
