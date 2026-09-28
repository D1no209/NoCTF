import { expect, test } from 'bun:test'
import { readFeatureSource as readFileSync } from './support/feature-source'

const read = (path: string) => readFileSync(new URL(`../app/${path}`, import.meta.url), 'utf8')

test('account information uses its own private SDK and string input without blocking participation', () => {
  const source = read('features/account/AccountPanel.vue')
  expect(source).toContain('authenticationGetMyProfile')
  expect(source).toContain('authenticationPatchMyProfile')
  expect(source).toContain('type="text"')
  expect(source).not.toContain('type="number"')
  expect(source).not.toContain('localStorage')
  expect(source).toContain('if (!identityLoaded.value || identityPending.value || !identityDirty.value) return')
  expect(source).toContain('identityFieldErrors')
})

test('the avatar popover replaces the account page with a two-by-two settings launcher', async () => {
  const panel = read('features/account/AccountPanel.vue')
  const layout = read('layouts/default.vue')

  expect(await Bun.file(new URL('../app/pages/account/index.vue', import.meta.url)).exists()).toBe(false)
  expect(panel).toContain('<Popover')
  expect(panel).toContain('data-slot="account-panel-menu"')
  expect(panel).toContain('data-slot="account-panel-detail"')
  expect(panel.match(/data-slot="account-panel-section-button"/g)?.length).toBe(4)
  expect(panel).toContain('body: { profile: { description: draft || null } }')
  expect(panel).toContain('profileError')
  expect(panel).toContain('@submit.prevent="changePassword"')
  expect(panel).toContain("activeSection.value === section ? null : section")
  expect(panel).toContain('authenticationUploadMyWallpaper')
  expect(panel).toContain('authenticationPatchMyProfile')
  expect(panel).toContain('grid min-h-0 flex-1 grid-cols-2 grid-rows-2')
  expect(layout).toContain(':is="AccountPanel"')
  expect(layout).not.toContain('<DropdownMenu>')
  expect(layout).not.toContain('to="/account"')
})

test('platform management uses the custom gear immediately before the message center', () => {
  const layout = read('layouts/default.vue')
  const gear = read('components/ui/icons/PlatformGearIcon.vue')
  const gearAsset = read('assets/svg/navigation/platform-gear.svg')
  const platformIndex = layout.indexOf("to: '/admin/platform'")
  const notificationsIndex = layout.indexOf("to: '/notifications'")

  expect(platformIndex).toBeGreaterThan(0)
  expect(platformIndex).toBeLessThan(notificationsIndex)
  expect(layout).toContain('markRaw(PlatformGearIconComponent)')
  expect(gear).toContain('data-slot="platform-gear-icon"')
  expect(gearAsset).toContain('<svg')
  expect(gearAsset).toContain('<circle')
  expect(gearAsset.match(/<rect /g)?.length).toBe(8)
  expect(gearAsset).toContain('rotate(315 12 12)')
})

test('light and dark non-home pages use local wallpapers with a configurable shared default', async () => {
  const layout = read('layouts/default.vue')
  const css = read('assets/css/main.css')

  expect(layout).toContain('data-slot="page-wallpaper"')
  expect(layout).toContain('v-if="!isHome"')
  expect(css).toContain("url('../images/backgrounds/light-pages-wallpaper.jpg')")
  expect(css).toContain("url('../images/backgrounds/dark-pages-wallpaper.jpg')")
  expect(layout).toContain(':data-personal-wallpaper="wallpaperActive || undefined"')
  expect(css).toContain("[data-page-wallpaper='true'][data-personal-wallpaper='true']")
  expect(css).toContain("[data-page-wallpaper='true']::before")
  expect(css).toContain("[data-page-wallpaper='true']::after")
  expect(css).toContain('filter: blur(var(--page-wallpaper-blur));')
  expect(await Bun.file(new URL('../app/assets/images/backgrounds/dark-pages-wallpaper.jpg', import.meta.url)).exists()).toBe(true)
})

test('private views are confined to staff pages and clear stale member responses', () => {
  expect(read('pages/admin/competitions/[id]/teams.vue')).toContain('<AccordionTrigger v-if="canJudge">')
  expect(read('pages/admin/competitions/[id]/teams.vue')).toContain('v-if="expandedMemberId === member.userId && member.userId && selectedTeam.id"')
  expect(read('features/account/PrivateAccountPanel.vue')).toContain('if (ticket !== revision) return')
  expect(read('pages/users/[id].vue')).not.toContain('PrivateAccountPanel')
  expect(read('pages/competitions/[id]/teams/[teamId].vue')).not.toContain('PrivateAccountPanel')
})
