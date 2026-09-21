import { expect, test } from 'bun:test'
import { resolveCompetitionBrowser } from '../app/features/competitions/competition-browser'
import { motionAttributes } from '../app/motion/presets'
import { useContentSwap } from '../app/motion/useContentSwap'
import {
  competitionChallengePath,
  competitionPath,
  competitionsPath,
} from '../app/utils/app-routes'

const items = [
  { id: 'live', title: 'Live', status: 'Running' as const, endTime: '2026-10-01' },
  { id: 'next', title: 'Next', status: 'Published' as const, startTime: '2026-11-01' },
  { id: 'past', title: 'Past', status: 'Finished' as const, endTime: '2026-01-01' },
]

test('competition entities use canonical path segments', () => {
  expect(competitionsPath).toBe('/competitions')
  expect(competitionPath('competition 1')).toBe('/competitions/competition%201')
  expect(competitionChallengePath('competition 1', 'challenge/1'))
    .toBe('/competitions/competition%201/challenges/challenge%2F1')
})

test('competition overview and challenge routes render canonical pages', async () => {
  const overview = await Bun.file(
    new URL('../app/pages/competitions/[id]/index.vue', import.meta.url),
  ).text()
  const challenges = await Bun.file(
    new URL('../app/pages/competitions/[id]/challenges/[[ccId]].vue', import.meta.url),
  ).text()
  expect(overview).toContain('CompetitionsIndexPage.vue')
  expect(overview).not.toContain('redirect:')
  expect(challenges).toContain('CompetitionsByIdChallengesIndexPage.vue')
  expect(challenges).toContain('key: route => route.params.id as string')
})

test('competition deep links select the right category and details', () => {
  const view = resolveCompetitionBrowser(items, 'past', 'running')
  expect(view.group).toBe('finished')
  expect(view.selected?.id).toBe('past')
  expect(view.items.map(c => c.id)).toEqual(['past'])
})

test('empty categories, initial selection and unavailable links remain distinct', () => {
  expect(resolveCompetitionBrowser(items, null, null).selected?.id).toBe('live')
  expect(resolveCompetitionBrowser(items.slice(1), null, null).selected?.id).toBe('next')
  const empty = resolveCompetitionBrowser(items.slice(1), null, 'running')
  expect(empty.selected).toBeNull()
  expect(empty.missing).toBe(false)
  const unavailable = resolveCompetitionBrowser(items, 'removed', 'running')
  expect(unavailable.selected).toBeNull()
  expect(unavailable.missing).toBe(true)
})

test('administrator browsing includes draft and deleted competitions without exposing them publicly', () => {
  const managed = [
    ...items,
    { id: 'draft', title: 'Draft', status: 'Draft' as const, startTime: '2026-12-01' },
    { id: 'deleted', title: 'Deleted', status: 'Finished' as const, deletedAt: '2026-08-01', endTime: '2026-07-01' },
  ]
  expect(resolveCompetitionBrowser(managed, 'draft', null, true).group).toBe('upcoming')
  expect(resolveCompetitionBrowser(managed, 'deleted', null, true).group).toBe('deleted')
  expect(resolveCompetitionBrowser(managed, 'draft', null).selected).toBeNull()
  expect(resolveCompetitionBrowser(managed, 'deleted', null).selected).toBeNull()
})

test('motion delays are bounded for long lists and animations live outside the primitive', async () => {
  expect(motionAttributes('list-enter', 100).style['--motion-stagger']).toBe('150ms')
  expect(motionAttributes('list-enter', -1).style['--motion-stagger']).toBe('0ms')
  const component = await Bun.file(new URL('../app/components/ui/selection-list/SelectionList.vue', import.meta.url)).text()
  expect(component).not.toContain('@keyframes')
  expect(component).not.toContain('window.addEventListener')
  const css = await Bun.file(new URL('../app/motion/motion.css', import.meta.url)).text()
  expect(css).toContain('prefers-reduced-motion: reduce')
})

test('film swaps leave before entering and release the reserved height after completion or cancellation', () => {
  const motion = useContentSwap('film-up')
  const outgoing = { getBoundingClientRect: () => ({ height: 795.4 }) } as Element
  expect(motion.transition.mode).toBe('out-in')
  motion.transition.onBeforeLeave(outgoing)
  expect(motion.frameStyle.value.minHeight).toBe('796px')
  motion.transition.onAfterEnter()
  expect(motion.frameStyle.value.minHeight).toBeUndefined()
  motion.transition.onBeforeLeave(outgoing)
  motion.transition.onEnterCancelled()
  expect(motion.frameStyle.value.minHeight).toBeUndefined()
})

test('competition details keep the opaque hero outside the scrolling body', async () => {
  const page = await Bun.file(new URL('../app/components/views/page/competitions/CompetitionsIndexPageView.vue', import.meta.url)).text()
  const overview = await Bun.file(new URL('../app/components/views/competitions/CompetitionOverviewView.vue', import.meta.url)).text()
  const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(page).not.toContain('<ScrollSurface :key="selectedId')
  expect(overview.indexOf('data-slot="competition-overview-hero"')).toBeLessThan(overview.indexOf('<ScrollSurface axis="y"'))
  expect(overview).toContain('data-slot="competition-overview-body"')
  expect(css).toContain("[data-slot='competition-overview-hero'] { background: var(--card); }")
  expect(css).toContain('--card-opacity: 45%;')
})
