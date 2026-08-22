import { describe, expect, test } from 'bun:test'
import { readProtectedDownload } from '../app/utils/download'

describe('protected downloads', () => {
  test('returns the generated client blob and decodes the response file name', async () => {
    const blob = new Blob(['payload'], { type: 'application/octet-stream' })
    let calls = 0

    const result = await readProtectedDownload(async () => {
      calls++
      return {
        data: blob,
        response: new Response(null, {
          status: 200,
          headers: {
            'content-disposition': "attachment; filename*=UTF-8''report%20data.zip",
          },
        }),
      }
    }, 'fallback.zip')

    expect(calls).toBe(1)
    expect(result.blob).toBe(blob)
    expect(result.fileName).toBe('report data.zip')
  })

  test('keeps the fallback name when content disposition is absent', async () => {
    const result = await readProtectedDownload(async () => ({
      data: new Blob(['payload']),
      response: new Response(null, { status: 200 }),
    }), 'fallback.bin')

    expect(result.fileName).toBe('fallback.bin')
  })

  test('preserves generated client problem metadata while localizing its message', async () => {
    expect(readProtectedDownload(async () => ({
      error: { status: 403, detail: 'download forbidden' },
      response: new Response(null, { status: 403 }),
    }))).rejects.toMatchObject({
      name: 'ApiError',
      status: 403,
      message: '没有权限执行此操作',
    })
  })

  test('all protected download pages use generated SDK operations', async () => {
    const paths = [
      '../app/components/challenges/CompetitionChallengeDetail.vue',
      '../app/pages/admin/platform/logs.vue',
      '../app/pages/admin/platform/audit.vue',
      '../app/pages/admin/competitions/[id]/exports.vue',
    ]
    const sources = await Promise.all(paths.map(path => Bun.file(new URL(path, import.meta.url)).text()))
    const source = sources.join('\n')

    expect(source).not.toContain('downloadProtectedFile(`/api/')
    expect(source).not.toContain('getAccessToken')
    expect(source).toContain('downloadChallengeAttachmentEndpoint')
    expect(source).toContain('downloadRandomChallengeAttachmentEndpoint')
    expect(source).toContain('adminPlatformExportLogs')
    expect(source).toContain('adminDownloadDataExport')
    expect(source).toContain('adminExportCompetitionEvents')
  })
})
