import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('authentication SDK contract usage', () => {
  test('refreshes through the generated endpoint and does not classify requests by API path', async () => {
    const session = await sourceFile(new URL('../app/lib/session.ts', import.meta.url)).text()
    const refreshPolicy = await sourceFile(new URL('../app/lib/auth-refresh.ts', import.meta.url)).text()
    const errors = await sourceFile(new URL('../app/utils/api-error.ts', import.meta.url)).text()
    const clientPlugin = await sourceFile(new URL('../app/plugins/api.client.ts', import.meta.url)).text()

    expect(session).toContain('refreshClient.api.v1.auth.refresh.post()')
    expect(session).not.toContain("fetch('/api/")
    expect(refreshPolicy).not.toContain('/api/')
    expect(refreshPolicy).toContain("requestHeaders.has('Authorization')")
    expect(errors).not.toContain("includes('/auth/")
    expect(clientPlugin).toContain('initializeApiClient(')
    expect(errors).toContain('problem.code')
    expect(errors).toContain('problem.responseStatusCode')
  })
})
