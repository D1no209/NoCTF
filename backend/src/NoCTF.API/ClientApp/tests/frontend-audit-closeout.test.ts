import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('frontend audit closeout', () => {
  test('platform logs default to Warning', async () => {
    const page = await sourceFile(new URL('../app/pages/admin/platform/logs.vue', import.meta.url)).text()
    expect(page).toContain("ref<NoCtfapiEndpointsAdministrationPlatformPlatformLogLevelProtocol>('Warning')")
  })

  test('definition editor delete buttons expose localized accessible names', async () => {
    const paths = [
      '../app/components/ui/list-editor/KeyValueEditor.vue',
      '../app/features/admin/UrlBindingList.vue',
      '../app/components/ui/list-editor/StringListEditor.vue',
      '../app/components/ui/list-editor/NumberListEditor.vue',
    ]

    for (const path of paths) {
      const component = await sourceFile(new URL(path, import.meta.url)).text()
      expect(component).toContain(":aria-label=\"$t('ui.removeItem', { index: index + 1 })\"")
      expect(component).toMatch(/<X class="size-4" aria-hidden="true" \/>/)
    }
  })

  test('SMTP password replacement reuses the password visibility control', async () => {
    const page = await sourceFile(new URL('../app/pages/admin/platform/email.vue', import.meta.url)).text()
    expect(page).toContain('<PasswordInput id="smtp-password"')
    expect(page).not.toContain('<Input id="smtp-password"')
  })
})
