import { describe, expect, test } from 'bun:test'

describe('generated protocol usage', () => {
  test('does not mirror the generated account role as a runtime enum', async () => {
    const source = await Bun.file(new URL('../app/composables/useAuth.ts', import.meta.url)).text()

    expect(source).not.toContain('export const UserRole')
    expect(source).toContain("user.value?.role === 'Administrator'")
    expect(source).toContain("user.value?.role === 'Organizer'")
  })
})
