import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const pagePaths = [
  '../app/pages/admin/platform/logs.vue',
  '../app/pages/admin/platform/audit.vue',
  '../app/pages/admin/competitions/[id]/exports.vue',
]

const challengePath = '../app/features/challenges/CompetitionChallengeDetail.vue'

describe('generated SDK file downloads', () => {
  test('keeps authentication and endpoint construction in the generated client', async () => {
    const helper = await sourceFile(new URL('../app/utils/download.ts', import.meta.url)).text()

    expect(helper).not.toContain('getAccessToken')
    expect(helper).not.toContain('fetch(')
    expect(helper).toContain('await response.blob()')

    for (const path of pagePaths) {
      const source = await sourceFile(new URL(path, import.meta.url)).text()
      expect(source).not.toContain('/api/v1')
      expect(source).not.toContain('fetch(')
      expect(source).toContain('nativeResponse(')
      expect(source).toContain('downloadSdkFile(')
    }

    const challenge = await sourceFile(new URL(challengePath, import.meta.url)).text()
    expect(challenge).not.toContain('/api/v1')
    expect(challenge).not.toContain('fetch(')
    expect(challenge).toContain('() => nativeResponse(')
    expect(challenge).toContain('downloadSdkFileToDisk(')
    expect(challenge.match(/if \(downloading\.value\) return/g)?.length).toBe(2)
    expect(helper).toContain('if (!response.body)')
    expect(helper).toContain('response.body.pipeTo(await handle.createWritable())')
  })

  test('uses the generated endpoint for each supported download', async () => {
    const logs = await sourceFile(new URL(pagePaths[0]!, import.meta.url)).text()
    const audit = await sourceFile(new URL(pagePaths[1]!, import.meta.url)).text()
    const exportsPage = await sourceFile(new URL(pagePaths[2]!, import.meta.url)).text()
    const challenge = await sourceFile(new URL(challengePath, import.meta.url)).text()

    expect(logs).toMatch(/api\.api\.v1\.admin\.platform\.logs\.exportEscaped\.get\(/)
    expect(audit).toMatch(/api\.api\.v1\.admin\.platform\.auditLogs\.dataExport\.post\(/)
    expect(exportsPage).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.events\.exportEscaped\.get\(/)
    expect(exportsPage).toMatch(/api\.api\.v1\.admin\.competitions\.byCompetitionId\([^)]*\)\.dataExport\.post\(/)
    expect(challenge).toMatch(/api\.api\.v1\.competitions\.byCompetitionId\([^)]*\)\.challenges\.byCompetitionChallengeId\([^)]*\)\.attachments\.byAttachmentId\([^)]*\)\.get\(/)
    expect(challenge).toMatch(/api\.api\.v1\.competitions\.byCompetitionId\([^)]*\)\.challenges\.byCompetitionChallengeId\([^)]*\)\.attachment\.get\(/)
  })
})
