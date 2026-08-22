import { describe, expect, test } from 'bun:test'

const pagePaths = [
  '../app/pages/admin/platform/logs.vue',
  '../app/pages/admin/platform/audit.vue',
  '../app/pages/admin/competitions/[id]/exports.vue',
  '../app/components/challenges/CompetitionChallengeDetail.vue',
]

describe('generated SDK file downloads', () => {
  test('keeps authentication and endpoint construction in the generated client', async () => {
    const helper = await Bun.file(new URL('../app/utils/download.ts', import.meta.url)).text()

    expect(helper).not.toContain('getAccessToken')
    expect(helper).not.toContain('fetch(')
    expect(helper).toContain('data instanceof Blob')

    for (const path of pagePaths) {
      const source = await Bun.file(new URL(path, import.meta.url)).text()
      expect(source).not.toContain('/api/v1')
      expect(source).not.toContain('fetch(')
      expect(source).toContain("parseAs: 'blob'")
      expect(source).toContain('downloadSdkFile(')
    }
  })

  test('uses the generated endpoint for each supported download', async () => {
    const logs = await Bun.file(new URL(pagePaths[0]!, import.meta.url)).text()
    const audit = await Bun.file(new URL(pagePaths[1]!, import.meta.url)).text()
    const exportsPage = await Bun.file(new URL(pagePaths[2]!, import.meta.url)).text()
    const challenge = await Bun.file(new URL(pagePaths[3]!, import.meta.url)).text()

    expect(logs).toContain('adminPlatformExportLogs({')
    expect(audit).toContain('adminDownloadDataExport({')
    expect(exportsPage).toContain('adminExportCompetitionEvents({')
    expect(exportsPage).toContain('adminDownloadDataExport({')
    expect(challenge).toContain('downloadChallengeAttachmentEndpoint({')
    expect(challenge).toContain('downloadRandomChallengeAttachmentEndpoint({')
  })
})
