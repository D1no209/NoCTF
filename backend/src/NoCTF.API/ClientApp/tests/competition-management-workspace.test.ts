import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('competition management keeps navigation fixed and scrolls animated content', async () => {
  const shell = await sourceFile(
    new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
  ).text()
  const main = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(shell).toContain('const activePath = computed(() => route.path)')
  expect(shell).toContain('data-workspace-scroll-content')
  expect(shell).toContain('data-competition-management-workspace')
  expect(shell).toContain('<ScrollSurface axis="y"')
  expect(shell).toContain('min-h-0 flex-1 overscroll-contain')
  expect(shell).toContain('<MotionSwap :identity="activePath" preset="film-up">')
  expect(main).toContain("[data-slot='app-workspace-nav']:has([data-workspace-scroll-content])")
  expect(main).toContain('grid-template-rows: minmax(10rem, 32dvh) minmax(0, 1fr)')
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
