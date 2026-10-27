import { expect, test } from 'bun:test'
import { effectScope, nextTick, reactive } from 'vue'
import { useLiveSoloMatchDirections, type LiveSoloMatchDirectionsProps } from '../app/features/live-solo/useLiveSoloMatchDirections'
import { directionPoolCounts } from '../app/features/live-solo/direction-pool'

test('match snapshots and round updates preserve an open direction editor and unsaved selections', async () => {
  const props = reactive<LiveSoloMatchDirectionsProps>({ competitionId: 'competition',
    match: { id: 'match', concurrencyStamp: 'first', requiredWins: 4 }, round: null, staff: true, members: [] })
  const scope = effectScope()
  const page = scope.run(() => useLiveSoloMatchDirections(props, () => {}))!
  try {
    page.editPreset()
    page.presetName.value = 'Web A'; page.direction.value = 'Web'; page.questionGroupId.value = 'web-a'
    page.togglePreset('web-a-preset', true)
    page.toggleGroup('web-a', true)
    page.groupSearch.value = 'Web'
    page.decisionReasons.value.request = 'Keep this referee note'

    props.match = { ...props.match }
    await nextTick()
    expect(page.editorOpen.value).toBe(true)
    expect(page.selectedCount.value).toBe(1)
    expect(page.batchSelectedCount.value).toBe(1)

    props.match = { ...props.match, concurrencyStamp: 'second' }
    props.round = { id: 'round', state: 'Preparing' }
    await nextTick()
    expect(page.editorOpen.value).toBe(true)
    expect(page.presetName.value).toBe('Web A')
    expect(page.direction.value).toBe('Web')
    expect(page.questionGroupId.value).toBe('web-a')
    expect(page.selectedCount.value).toBe(1)
    expect(page.decisionReasons.value.request).toBe('Keep this referee note')
    expect(page.batchSelectedCount.value).toBe(1)
    expect(page.groupSearch.value).toBe('Web')

    props.match = { ...props.match, id: 'another-match' }
    await nextTick()
    expect(page.editorOpen.value).toBe(false)
    expect(page.selectedCount.value).toBe(0)
    expect(page.batchSelectedCount.value).toBe(0)
    expect(page.decisionReasons.value).toEqual({})
  } finally { scope.stop() }
})

test('remaining tickets are grouped by direction and exclude drawn or ineligible lists', () => {
  const pool = [
    { direction: 'Web', available: true, drawnAt: null },
    { direction: 'Web', available: true, drawnAt: null },
    { direction: 'PWN', available: true, drawnAt: null },
    { direction: 'PWN', available: false, drawnAt: null },
    { direction: 'Crypto', available: false, drawnAt: '2026-10-28T00:00:00Z' },
  ]
  expect(directionPoolCounts(pool)).toEqual([
    { direction: 'Web', remaining: 2 }, { direction: 'PWN', remaining: 1 }, { direction: 'Crypto', remaining: 0 },
  ])
  pool[0] = { ...pool[0]!, available: false, drawnAt: '2026-10-28T00:01:00Z' }
  expect(directionPoolCounts(pool)[0]).toEqual({ direction: 'Web', remaining: 1 })
})
