import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const generatedSdk = await Bun.file(
  new URL('../src/api/generated/sdk.gen.ts', import.meta.url),
).text()
const generatedTypes = await Bun.file(
  new URL('../src/api/generated/types.gen.ts', import.meta.url),
).text()
const apiSource = await Bun.file(new URL('../src/api/noctf.ts', import.meta.url)).text()
const routerSource = await Bun.file(new URL('../src/router/index.ts', import.meta.url)).text()
const brandLogoSource = await Bun.file(new URL('../src/components/BrandLogo.vue', import.meta.url)).text()
const informationSource = await Bun.file(
  new URL('../src/components/admin/settings/AdminPlatformInformationWorkspace.vue', import.meta.url),
).text()

describe('platform configuration', () => {
  test('uses generated public and administrator contracts', () => {
    for (const operation of [
      'platformConfigurationGet',
      'adminPlatformGetConfiguration',
      'adminPlatformUpdateConfiguration',
      'adminPlatformUploadLogo',
      'adminPlatformGetInformation',
    ]) {
      expect(generatedSdk).toContain(`export const ${operation}`)
      expect(apiSource).toContain(`generatedSdk.${operation}`)
    }

    expect(generatedTypes).toContain('url: \'/api/v1/platform/configuration\'')
    expect(generatedTypes).toContain('url: \'/api/v1/admin/platform/configuration/logo\'')
  })

  test('moves email verification into the settings sub-navigation and preserves the old URL', () => {
    expect(routerSource).toContain('path: \'settings\'')
    expect(routerSource).toContain('path: \'basic\'')
    expect(routerSource).toContain('path: \'email-verification\'')
    expect(routerSource).toContain('path: \'information\'')
    expect(routerSource).toContain('redirect: { name: \'admin-email-verification\' }')
  })

  test('binds the shared brand logo to server-provided branding', () => {
    expect(brandLogoSource).toContain('queryKeys.platformConfiguration')
    expect(brandLogoSource).toContain('platformApi.configuration')
    expect(brandLogoSource).toContain('configuration.value?.logoUrl')
  })

  test('shows contributor IDs and avatars without contribution counts', () => {
    expect(informationSource).toContain('contributor.avatarUrl')
    expect(informationSource).toContain('contributor.id')
    expect(informationSource).not.toContain('contributions')
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('provides complete settings navigation copy for %s', (locale, messages) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    for (const key of [
      'admin.nav.platformSettings',
      'admin.settings.title',
      'admin.settings.tabs.basic',
      'admin.settings.tabs.emailVerification',
      'admin.settings.tabs.information',
      'admin.settings.basic.logo',
      'admin.settings.basic.name',
      'admin.settings.information.version',
      'admin.settings.information.contributors',
    ]) {
      expect(i18n.global.t(key)).not.toBe(key)
    }
  })
})
