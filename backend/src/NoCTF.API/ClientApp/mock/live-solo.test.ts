import { describe, expect, test } from 'bun:test'
import { createMockApi } from './api'
import { createFixtures } from './data/fixtures'
import { createMockLiveSolo } from './live-solo'
import { id } from './schema'
import { groupDraft, validGroup } from '../app/features/live-solo/group-draft'

describe('isolated LiveSolo UI fixtures', () => {
  test('uses independent templates, teams and valid group schedules', () => {
    const state = createFixtures(), fixture = createMockLiveSolo(state)
    const challenges = state.challenges.filter(row => row.competitionId === fixture.competitionId)
    expect(challenges).toHaveLength(4)
    for (const row of challenges) expect(state.templates.find(template => template.id === row.challengeId)?.mode).toBe('LiveSolo')
    for (const group of fixture.groups) expect(validGroup(groupDraft(group), 180, 900)).toBe(true)
    expect(new Set(state.teams.map(row => row.id)).size).toBe(state.teams.length)
  })
  test('serves groups and matches through real route contracts without faking writes', async () => {
    const api = createMockApi()
    const base = `http://mock.invalid/api/v1/competitions/${id(2, 8)}/live-solo`
    const groups = await api.handle(new Request(`${base}/question-groups`))
    expect(groups.status).toBe(200)
    expect((await groups.json()).items).toHaveLength(3)
    const matches = await api.handle(new Request(`${base}/matches`))
    expect((await matches.json()).items.map((row: { state: string }) => row.state)).toEqual(['Preparing', 'Running', 'Completed'])
    const save = await api.handle(new Request(`${base}/question-groups`, { method: 'POST', body: '{}' }))
    expect(save.status).not.toBe(200)
  })
})
