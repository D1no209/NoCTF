import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('team runtime management', () => {
  test('mounts only for an approved, unbanned team and reuses the protected runtime controls', async () => {
    const page = await sourceFile(new URL('../app/components/views/page/competitions/[id]/my/CompetitionsByIdMyTeamPageView.vue', import.meta.url)).text()
    const manager = await sourceFile(new URL('../app/features/teams/useTeamRuntimeManager.ts', import.meta.url)).text()
    const view = await sourceFile(new URL('../app/components/views/teams/TeamRuntimeManagerView.vue', import.meta.url)).text()
    const card = await sourceFile(new URL('../app/features/challenges/useRuntimeCard.ts', import.meta.url)).text()

    expect(page).toContain("team.registrationStatus === 'Approved' && !team.isBanned")
    expect(manager).toContain('listMyTeamRuntimes({')
    expect(manager).toContain('useOffsetPagination<TeamRuntime>')
    expect(manager).toContain('onReconnected: () => { void refresh() }')
    expect(view).toContain(':is="RuntimeCard"')
    expect(view).toContain('controls="full"')
    expect(view).toContain('@changed="refresh"')
    expect(view).toContain('<OffsetPagination')
    expect(card).toContain("emit('changed')")
    expect(card).toContain('createRuntimeExtensionRequest(')
    expect(card).toContain("requestHumanVerification('runtime')")
  })
})
