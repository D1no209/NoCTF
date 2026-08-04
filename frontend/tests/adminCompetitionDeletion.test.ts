import { describe, expect, test } from 'bun:test'

const workspaceSource = await Bun.file(
  new URL('../src/components/admin/competitions/AdminCompetitionsWorkspace.vue', import.meta.url),
).text()

describe('admin competition deletion interaction', () => {
  test('opens destructive confirmation through the dropdown select event', () => {
    expect(workspaceSource).toContain('@select="openDelete(row.original)"')
    expect(workspaceSource).not.toContain('@click="openDelete(row.original)"')
  })

  test('confirms only when a competition remains selected', () => {
    expect(workspaceSource).toContain('function deleteSelectedCompetition()')
    expect(workspaceSource).toContain('@confirm="deleteSelectedCompetition"')
    expect(workspaceSource).not.toContain('selectedComp!.id')
  })
})
