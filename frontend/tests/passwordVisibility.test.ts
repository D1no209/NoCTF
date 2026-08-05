import { describe, expect, test } from 'bun:test'

const loginSource = await Bun.file(new URL('../src/components/auth/LoginWorkspace.vue', import.meta.url)).text()
const registerSource = await Bun.file(new URL('../src/components/auth/RegisterWorkspace.vue', import.meta.url)).text()
const profileSource = await Bun.file(new URL('../src/components/profile/ProfileWorkspace.vue', import.meta.url)).text()

describe('password visibility controls', () => {
  test.each([
    ['login', loginSource],
    ['registration', registerSource],
  ])('keeps the %s password toggle accessible and non-submitting', (_, source) => {
    expect(source).toContain('const passwordVisible = ref(false)')
    expect(source).toContain(':type="passwordVisible ? \'text\' : \'password\'"')
    expect(source).toContain(':aria-label="t(passwordVisible ? \'auth.hidePassword\' : \'auth.showPassword\')"')
    expect(source).toContain(':aria-pressed="passwordVisible"')
    expect(source).toContain('@click="passwordVisible = !passwordVisible"')
    expect(source).toContain('type="button"')
  })

  test('provides independent accessible toggles for all password-change fields', () => {
    for (const name of ['currentPassword', 'newPassword', 'confirmPassword']) {
      expect(profileSource).toContain(`const ${name}Visible = ref(false)`)
      expect(profileSource).toContain(`:type="${name}Visible ? 'text' : 'password'"`)
      expect(profileSource).toContain(`@click="${name}Visible = !${name}Visible"`)
    }
    expect(profileSource.match(/type="button"/g)).toHaveLength(3)
  })
})
