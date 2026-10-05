import { sourceFile } from './support/feature-source'
import { expect, test } from 'bun:test'
import { adminPatchFailureMessage } from '../app/utils/admin-patch'
import { parseApiError } from '../app/utils/api-error'
import { readProtectedDownload } from '../app/utils/download'

test('Patch downloads retain UTF-8 attachment names and original bytes', async () => {
  const bytes = new Uint8Array([0, 1, 255, 13, 10])
  const name = '原始 Patch.tar.gz'
  const result = await readProtectedDownload(() => Promise.resolve({
    data: new Blob([bytes]),
    response: new Response(null, { headers: { 'content-disposition': `attachment; filename="__ Patch.tar.gz"; filename*=UTF-8''${encodeURIComponent(name)}` } }),
  }))
  expect(result.fileName).toBe(name)
  expect(new Uint8Array(await result.blob.arrayBuffer())).toEqual(bytes)
})

test('Patch download failures retain clear Chinese diagnostics', () => {
  expect(adminPatchFailureMessage('PatchNotFound')).toBe('该提交没有 Patch 包。')
  expect(adminPatchFailureMessage('FileNotFound')).toBe('Patch 包文件已不存在。')
  expect(parseApiError({ messageKey: "common.patch.description.competitionSOwnerManagers",  status: 403, detail: "Only this competition's owner, managers, judges, or a platform administrator may download Patch archives." }).message)
    .toBe('只有本场比赛负责人、Manager、Judge 或平台管理员可以下载 Patch。')
  expect(parseApiError({ messageKey: "common.patch.description.patchDownloadAuditCould",  status: 503, detail: 'The Patch download audit could not be saved. No file was returned. Try again later.' }).message)
    .toBe('Patch 下载审计保存失败，未下发文件，请稍后重试。')
})

test('existing submissions page uses staff-gated SDK download without preview or internal credentials', async () => {
  const source = await sourceFile(new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url)).text()
  expect(source).toContain("canDownloadPatch && s.kind === 'FixAttempt'")
  expect(source).toContain("role.value === 'Owner' || role.value === 'Manager' || role.value === 'Judge'")
  expect(source).toContain('downloadSdkFile(adminDownloadGameplayFactPatch({')
  expect(source).toContain("parseAs: 'blob'")
  expect(source).toContain('patchDownloading.value.delete(id)')
  expect(source).toContain('detail.patch.fileName')
  expect(source).toContain('detail.patch.sha256')
  expect(source).toContain('request === detailRequest')
  expect(source).not.toContain('/api/internal')
  expect(source).not.toContain('fix-archives')
  expect(source).not.toContain('window.open(')
  expect(source).not.toContain('<iframe')
})
