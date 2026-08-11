import { describe, expect, test } from 'bun:test'

describe('authentication SDK contract usage', () => {
  test('refreshes through the generated endpoint and does not classify requests by API path', async () => {
    const session = await Bun.file(new URL('../app/lib/session.ts', import.meta.url)).text()
    const refreshPolicy = await Bun.file(new URL('../app/lib/auth-refresh.ts', import.meta.url)).text()

    expect(session).toContain('refreshTokenEndpoint({ client: refreshClient })')
    expect(session).not.toContain("fetch('/api/")
    expect(refreshPolicy).not.toContain('/api/')
    expect(refreshPolicy).toContain("requestHeaders.has('Authorization')")
  })
})
