import { describe, expect, test } from 'bun:test'

const generatedSdk = await Bun.file(
  new URL('../src/api/generated/sdk.gen.ts', import.meta.url),
).text()
const generatedTypes = await Bun.file(
  new URL('../src/api/generated/types.gen.ts', import.meta.url),
).text()
const apiSource = await Bun.file(new URL('../src/api/noctf.ts', import.meta.url)).text()
const workspaceSource = await Bun.file(
  new URL('../src/components/admin/users/AdminUsersWorkspace.vue', import.meta.url),
).text()

describe('administrator user deletion', () => {
  test('uses the generated preview and deletion contracts', () => {
    expect(generatedSdk).toContain('export const adminPlatformPreviewUserDeletion')
    expect(generatedSdk).toContain('export const adminPlatformDeleteUser')
    expect(generatedTypes).toContain(
      'url: \'/api/v1/admin/platform/users/{userId}/deletion-preview\'',
    )
    expect(generatedTypes).toContain('url: \'/api/v1/admin/platform/users/{userId}\'')
    expect(apiSource).toContain('generatedSdk.adminPlatformPreviewUserDeletion')
    expect(apiSource).toContain('generatedSdk.adminPlatformDeleteUser')
  })

  test('previews impact before exposing the bounded deletion actions', () => {
    expect(workspaceSource).toContain('@select="openDeletionDialog(row.original)"')
    expect(workspaceSource).toContain('platformAdminApi.previewUserDeletion(userId)')
    expect(workspaceSource).toContain('deleteUser(\'HardDelete\')')
    expect(workspaceSource).toContain('deleteUser(\'Anonymize\')')
    expect(workspaceSource).toContain('deletionPreview.references')
  })
})
