import { describe, expect, test } from 'bun:test'

describe('frontend audit closeout', () => {
  test('platform logs default to Warning', async () => {
    const page = await Bun.file(new URL('../app/pages/admin/platform/logs.vue', import.meta.url)).text()
    expect(page).toContain("ref<NoCtfapiEndpointsAdministrationPlatformPlatformLogLevelProtocol>('Warning')")
  })

  test('definition editor delete buttons expose localized accessible names', async () => {
    const paths = [
      '../app/components/admin/DefinitionCompose.vue',
      '../app/components/admin/KeyValueEditor.vue',
      '../app/components/admin/UrlBindingList.vue',
      '../app/components/admin/StringListEditor.vue',
      '../app/components/admin/NumberListEditor.vue',
    ]

    for (const path of paths) {
      const component = await Bun.file(new URL(path, import.meta.url)).text()
      expect(component).toContain(":aria-label=\"$t('移除第 {index} 项', { index: index + 1 })\"")
      expect(component).toMatch(/<X class="size-4" aria-hidden="true" \/>/)
    }
  })

  test('SMTP password replacement reuses the password visibility control', async () => {
    const page = await Bun.file(new URL('../app/pages/admin/platform/email.vue', import.meta.url)).text()
    expect(page).toContain('<PasswordInput id="smtp-password"')
    expect(page).not.toContain('<Input id="smtp-password"')
  })
})
