import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('competition management keeps navigation fixed and scrolls animated content', async () => {
  const shell = await sourceFile(
    new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
  ).text()
  const workspace = await Bun.file(
    new URL('../app/components/views/app/settings-workspace.css', import.meta.url),
  ).text()

  expect(shell).toContain('const activePath = computed(() => route.path)')
  expect(shell).toContain('data-workspace-scroll-content')
  expect(shell).toContain('data-competition-management-workspace')
  expect(shell).toContain('<ScrollSurface axis="y"')
  expect(shell).toContain('min-h-0 flex-1 overscroll-contain')
  expect(shell).toContain('<MotionSwap :identity="activePath" preset="film-up">')
  expect(workspace).toContain("[data-slot='app-workspace-nav']:has([data-workspace-scroll-content])")
  expect(workspace).toContain('grid-template-rows: minmax(10rem, 32dvh) minmax(0, 1fr)')
})

test('challenge, team and progression management use page scroll instead of nested workspace scroll', async () => {
  const shell = await sourceFile(
    new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
  ).text()
  const scrollSurface = await Bun.file(
    new URL('../app/components/ui/scroll-area/ScrollSurface.vue', import.meta.url),
  ).text()
  const layout = await sourceFile(
    new URL('../app/layouts/default.vue', import.meta.url),
  ).text()
  const workspace = await Bun.file(
    new URL('../app/components/views/app/settings-workspace.css', import.meta.url),
  ).text()

  expect(shell).toContain('activePath.value === `${base}/teams`')
  expect(shell).toContain('activePath.value === `${base}/challenges`')
  expect(shell).toContain('activePath.value.startsWith(`${base}/challenges/`)')
  expect(shell).toContain(":data-workspace-scroll-content=\"usesPageScroll ? undefined : ''\"")
  expect(shell).toContain(':enabled="!usesPageScroll"')
  expect(shell).toContain("usesPageScroll ? 'overflow-visible'")
  expect(layout).toContain('<ScrollSurface as="main" axis="y"')
  expect(workspace).toContain(":has([data-workspace-scroll-content])")
  expect(scrollSurface).toContain('enabled?: boolean')
  expect(scrollSurface).toContain('watch(() => props.enabled, updateEnabled')
  expect(scrollSurface).toContain('scrollbars?.dispose()')
})

test('related competition settings share continuous cards', async () => {
  const pages = await Promise.all([
    'index',
    'configuration',
    'leaderboard',
    'exports',
    'permissions',
  ].map(name => sourceFile(new URL(`../app/pages/admin/competitions/[id]/${name}.vue`, import.meta.url)).text()))

  for (const page of pages) expect(page.match(/<Card(?:\s|>)/g)).toHaveLength(1)
  expect(pages[0]).toContain('id="competition-lifecycle-management"')
  expect(pages[1]).toContain('id="competition-mode-configuration"')
  expect(pages[2]).toContain('id="competition-leaderboard-visibility"')
  expect(pages[3]).toContain('id="competition-archive-export"')
  expect(pages[4]).toContain('id="competition-ownership-transfer"')
  for (const page of pages) expect(page).toContain('<Separator')
})
