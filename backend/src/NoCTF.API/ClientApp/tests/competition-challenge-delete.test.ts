import { describe, expect, test } from 'bun:test'

describe('competition challenge deletion', () => {
  test('keeps the selected challenge while deletion is pending or fails', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    const deleteSection = page.slice(page.indexOf('// ---- Delete / restore ----'))

    expect(deleteSection).toContain('function closeDeleteDialog(open: boolean)')
    expect(deleteSection).toContain('if (!open && !deletePending.value)')
    expect(deleteSection).toContain('function beginDeleteChallenge(c: NoCtfapiEndpointsChallengesChallengeResponse)')
    expect(deleteSection).toContain('@click="beginDeleteChallenge(c)"')
    expect(deleteSection).toContain('const target = deleteTarget.value')
    expect(deleteSection).toContain('if (!target?.id || deletePending.value) return')
    expect(deleteSection).toContain('deletePending.value = true')
    expect(deleteSection).toContain('deleteError.value = parseApiError(e).message')
    expect(deleteSection).toContain('<Alert v-if="deleteError" variant="destructive">')
    expect(deleteSection).toContain(':disabled="deletePending"')
    expect(deleteSection).toContain('@click="removeChallenge"')
    expect(deleteSection).not.toMatch(/<AlertDialogAction[\s\S]*?removeChallenge/)
  })

  test('still calls the generated delete SDK with the selected revision', async () => {
    const page = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    const deleteHandler = page.slice(page.indexOf('async function removeChallenge()'), page.indexOf('async function restoreChallenge'))

    expect(deleteHandler).toContain('adminDeleteCompetitionChallenge')
    expect(deleteHandler).toContain('path: { competitionId, competitionChallengeId: target.id }')
    expect(deleteHandler).toContain('query: { expectedRevision: target.revision ?? 0 }')
    expect(deleteHandler).toContain('toast.success(translate("题目已删除"))')
    expect(deleteHandler).toContain('deleteTarget.value = null')
  })
})
