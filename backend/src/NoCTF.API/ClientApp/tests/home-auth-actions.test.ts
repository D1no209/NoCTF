import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('home authentication actions', () => {
  test('only offers registration to anonymous visitors', async () => {
    const page = await sourceFile(
      new URL('../app/pages/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('const { isLoggedIn, user } = useAuth()')
    expect(page).toContain('<Button v-if="!isLoggedIn" size="lg" variant="outline" as-child>')
    expect(page).toContain("<NuxtLink to=\"/auth/register\">{{ $t('ui.registerNow') }}</NuxtLink>")
    expect(page).toContain('<NuxtLink to="/competitions">')
  })
})
