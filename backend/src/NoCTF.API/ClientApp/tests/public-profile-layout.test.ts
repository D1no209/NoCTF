import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('public profile shifts its card right and centers the avatar above it', async () => {
  const profile = await sourceFile(
    new URL('../app/pages/users/[id].vue', import.meta.url),
  ).text()
  const main = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(profile).toContain('md:items-end')
  expect(profile).toContain('data-public-profile-card')
  expect(profile).toContain('data-public-profile-avatar')
  expect(profile).toContain('absolute top-0 left-1/2')
  expect(profile).toContain('-translate-x-1/2 -translate-y-1/2')
  expect(profile).toContain('items-center text-center')
  expect(main).toContain('[data-public-profile-avatar]')
  expect(main).toContain('box-shadow: var(--card-shadow)')
})
