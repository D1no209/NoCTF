import { expect, test } from 'bun:test'
import { buildProfileDirectionRows } from '../app/lib/profile-directions'
import { sourceFile } from './support/feature-source'

test('public profile uses equal-width layers with a fixed golden-ratio height split', async () => {
  const profile = await sourceFile(
    new URL('../app/pages/users/[id].vue', import.meta.url),
  ).text()
  const cropDialog = await sourceFile(
    new URL('../app/features/account/AvatarCropDialog.vue', import.meta.url),
  ).text()
  const main = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(profile).toContain('data-profile-identity-layer')
  expect(profile).toContain('data-profile-technical-layer')
  expect(profile).toContain('h-full min-h-0')
  expect(profile).toContain('overflow-hidden')
  expect(profile).toContain('grid-rows-[minmax(0,0.618fr)_minmax(0,1fr)]')
  expect(profile).toContain('data-profile-identity-layer class="relative h-full min-h-0 w-full')
  expect(profile).toContain('data-profile-technical-layer class="h-full min-h-0 w-full')
  expect(profile).toContain('data-public-profile-avatar')
  expect(profile).toContain('flex items-start justify-between')
  expect(profile).toMatch(/api\.api\.v1\.auth\.me\.profileCover\.put\(/)
  expect(profile).not.toContain('usePersonalWallpaper')
  expect(profile).not.toMatch(/api\.api\.v1\.auth\.me\.wallpaper\.put\(/)
  expect(profile).toContain('<div v-if="coverUrl" class="absolute inset-0 bg-card/70"')
  expect(profile).not.toContain('profile.coverUploadHint')
  expect(profile).toContain('<ScrollSurface v-if="recentCompetitions.length"')
  expect(profile.match(/<MiniChart/g)).toHaveLength(2)
  expect(profile).toContain('modeChartOption')
  expect(profile).toContain('directionChartOption')
  expect(profile).toContain('buildProfileDirectionRows(directions.value)')
  expect(profile).not.toContain('directionRows.value.slice')
  expect(profile).toContain('shape: \'polygon\'')
  expect(profile).toContain('min: -visualInset')
  expect(profile).toContain('highest > 0 ? Math.max(1, Math.ceil(highest * 0.18)) : 0')
  expect(profile).toContain('v-if="directionRows.length >= 3"')
  expect(profile).toContain('v-else-if="directionRows.length"')
  expect(profile).toContain('<MiniChart :option="directionChartOption"')
  expect(profile).toContain('color-token="--primary"')
  expect(profile).toContain('modeRows')
  expect(profile).toContain('data-profile-earned-badges')
  expect(profile).toContain('<Popover v-for="badge in profile.badges"')
  expect(profile).toContain('{{ badge.competitionTitle }}')
  expect(profile).toContain('v-if="badge.description"')
  expect(profile).not.toContain('<ScrollSurface v-if="profile.badges?.length"')
  expect(profile).toContain('variant="profile-cover"')
  expect(profile).toContain('@save="uploadCover"')
  expect(profile).toContain('coverEditorOpen.value = true')
  expect(profile).toContain('.profileCover.put(await multipartBody({ file }))')
  expect(cropDialog).toContain('data-profile-cover-crop')
  expect(main).toContain('[data-public-profile-avatar]')
  expect(main).toContain('box-shadow: var(--card-shadow)')
})

test('profile radar axes contain only historical competition directions, including zero scores', () => {
  expect(buildProfileDirectionRows([
    { direction: 'Pwn', successfulChallengeCount: 5 },
    { direction: 'Web', successfulChallengeCount: 0 },
    { direction: 'pwn', successfulChallengeCount: 1 },
    { direction: 'Custom', successfulChallengeCount: 0 },
  ])).toEqual([
    { direction: 'web', label: 'Web', successfulChallengeCount: 0 },
    { direction: 'pwn', label: 'Pwn', successfulChallengeCount: 6 },
    { direction: 'custom', label: 'Custom', successfulChallengeCount: 0 },
  ])
})

test('account popover is a compact square launcher with a profile entry', async () => {
  const account = await sourceFile(
    new URL('../app/features/account/AccountPanel.vue', import.meta.url),
  ).text()

  expect(account).toContain("wideAccountPanel = useMediaQuery('(min-width: 1024px)')")
  expect(account).toContain(":align=\"wideAccountPanel ? 'center' : 'end'\"")
  expect(account).toContain(":collision-padding=\"wideAccountPanel ? 0 : 12\"")
  expect(account).toContain('w-[min(20rem,calc(100vw-1rem))]')
  expect(account).not.toContain('<FieldDescription>{{ avatarRequirements }}</FieldDescription>')
  expect(account).not.toContain("$t('accountPanel.wallpaperSwitchDescription')")
  expect(account).not.toContain('{{ wallpaperRequirements }}')
  expect(account).toContain('grid min-h-0 flex-1 grid-cols-2 grid-rows-2')
  expect(account).toContain('data-slot="account-panel-menu" class="h-[min(20rem,calc(100vw-1rem))] w-full')
  expect(account).toContain('data-slot="account-panel-detail" class="noctf-motion-detail-enter min-h-0 w-full')
  expect(account).toContain(':to="`/users/${user.userId}`"')
  expect(account).toContain(":aria-label=\"$t('profile.openProfile')\"")
  expect(account).not.toContain("{{ $t('profile.openProfile') }}")
})

test('profile direction labels preserve configured capitalization', () => {
  expect(buildProfileDirectionRows([{ direction: 'Web / Research Lab', successfulChallengeCount: 2 }])[0]?.label).toBe('Web / Research Lab')
})
