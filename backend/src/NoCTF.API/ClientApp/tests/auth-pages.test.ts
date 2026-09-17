import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('authentication pages and dialog surfaces', () => {
  test('keeps login and registration on widened standalone cards', async () => {
    const loginRoute = await Bun.file(new URL('../app/pages/auth/login.vue', import.meta.url)).text()
    const registerRoute = await Bun.file(new URL('../app/pages/auth/register.vue', import.meta.url)).text()
    const login = await sourceFile(new URL('../app/pages/auth/login.vue', import.meta.url)).text()
    const register = await sourceFile(new URL('../app/pages/auth/register.vue', import.meta.url)).text()
    const loginAsset = Bun.file(new URL('../app/assets/images/auth/login-character.png', import.meta.url))
    const registerAsset = Bun.file(new URL('../app/assets/images/auth/register-character.png', import.meta.url))

    expect(loginRoute).toContain("middleware: 'guest'")
    expect(registerRoute).toContain("middleware: 'guest'")
    expect(login).toContain('@submit.prevent="submit"')
    expect(register).toContain('authenticationRequestEmailVerification')
    expect(login).toContain('max-w-xl')
    expect(register).toContain('max-w-xl')
    expect(login).toContain('<Card class="auth-card">')
    expect(register).toContain('<Card v-else class="auth-card">')
    expect(login).toContain('v-if="authArtwork"')
    expect(login).toContain(':data-corner="authArtwork.corner"')
    expect(register).toContain('v-if="authArtwork"')
    expect(register).toContain(':data-corner="authArtwork.corner"')
    expect(login).not.toContain('data-theme-character')
    expect(register).not.toContain('data-theme-character')
    expect(login).not.toContain('AuthCharacterOrnament')
    expect(register).not.toContain('AuthCharacterOrnament')
    expect(await loginAsset.exists()).toBe(true)
    expect(await registerAsset.exists()).toBe(true)
    expect(new Uint8Array(await loginAsset.arrayBuffer())[25]).toBe(6)
    expect(new Uint8Array(await registerAsset.arrayBuffer())[25]).toBe(6)
    expect(login).not.toContain("$t('ui.logInToYourAccountUsingYourUsernameOrEmail')")
    expect(login).toContain('<CardTitle class="text-xl font-semibold">')
    expect(login).toContain('<CardHeader class="relative z-10 pt-3">')
    expect(login).toContain('sm:mx-auto sm:w-2/3')
    expect(register).not.toContain("$t('ui.createANewAccountToEnterTheContest')")
    expect(register).toContain('<CardTitle class="text-xl font-semibold">')
    expect(register).toContain('<CardHeader class="relative z-10 pt-3">')
    expect(register).toContain('sm:mx-auto sm:w-2/3')
  })

  test('renders dialog and confirmation surfaces through the Card primitive', async () => {
    const dialog = await Bun.file(new URL('../app/components/ui/dialog/DialogContent.vue', import.meta.url)).text()
    const scrollingDialog = await Bun.file(new URL('../app/components/ui/dialog/DialogScrollContent.vue', import.meta.url)).text()
    const alertDialog = await Bun.file(new URL('../app/components/ui/alert-dialog/AlertDialogContent.vue', import.meta.url)).text()

    for (const source of [dialog, scrollingDialog, alertDialog]) {
      expect(source).toContain("import { Card } from '~/components/ui/card'")
      expect(source).toContain('<Card')
      expect(source).toContain('data-modal-scroll-lock')
    }
  })
})
