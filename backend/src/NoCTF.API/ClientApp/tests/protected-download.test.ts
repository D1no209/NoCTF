import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { startAttachmentBrowserDownload, startTeamWriteUpBrowserDownload, readProtectedDownload } from '../app/utils/download'

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

  test('hands a file URL to the browser without opening a system picker or materializing file bytes', () => {
    const clicked: string[] = []
    const originals = ['window', 'document', 'showSaveFilePicker'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)] as const)
    Object.defineProperty(globalThis, 'window', { configurable: true, value: { location: { origin: 'https://noctf.test' } } })
    Object.defineProperty(globalThis, 'document', { configurable: true, value: {
      body: { append: () => {} }, createElement: () => ({ href: '', download: '', hidden: false, click() { clicked.push(this.href) }, remove() {} }),
    } })
    Object.defineProperty(globalThis, 'showSaveFilePicker', { configurable: true, value: () => { throw new Error('System picker must not be called') } })
    try {
      startAttachmentBrowserDownload('/api/v1/attachments/demo')
      expect(clicked).toEqual(['https://noctf.test/api/v1/attachments/demo'])
      expect(() => startAttachmentBrowserDownload('https://evil.test/file')).toThrow()
      expect(() => startAttachmentBrowserDownload('/file?access_token=secret')).toThrow()
      expect(() => startAttachmentBrowserDownload('javascript:alert(1)')).toThrow()
      const own = '/api/v1/competitions/11111111-1111-1111-1111-111111111111/teams/me/writeup/content'
      const staff = own.replace('/me/', '/22222222-2222-2222-2222-222222222222/')
      expect(startTeamWriteUpBrowserDownload(own)).toBeUndefined()
      expect(startTeamWriteUpBrowserDownload(staff)).toBeUndefined()
      expect(clicked.slice(-2)).toEqual(['https://noctf.test' + own, 'https://noctf.test' + staff])
      expect(() => startTeamWriteUpBrowserDownload('https://evil.test' + own)).toThrow()
      expect(() => startTeamWriteUpBrowserDownload(own + '?access_token=secret')).toThrow()
      expect(() => startTeamWriteUpBrowserDownload('/api/v1/admin/platform/users')).toThrow()
    } finally {
      for (const [key, original] of originals) { if (original) Object.defineProperty(globalThis, key, original); else Reflect.deleteProperty(globalThis, key) }
    }
  })

  test('all protected download pages use generated SDK operations', async () => {
    const paths = [
      '../app/features/challenges/CompetitionChallengeDetail.vue',
      '../app/pages/admin/platform/logs.vue',
      '../app/pages/admin/platform/audit.vue',
      '../app/pages/admin/competitions/[id]/exports.vue',
    ]
    const sources = await Promise.all(paths.map(path => sourceFile(new URL(path, import.meta.url)).text()))
    const source = sources.join('\n')

    expect(source).not.toContain('downloadProtectedFile(`/api/')
    expect(source).not.toContain('getAccessToken')
    expect(source).toContain('prepareChallengeAttachmentDownloadEndpoint')
    expect(source).toContain('prepareRandomChallengeAttachmentDownloadEndpoint')
    expect(source).toContain('adminPlatformExportLogs')
    expect(source).toContain('adminExportCompetitionArchive')
    expect(source).toContain('adminExportPlatformAuditArchive')
    expect(source).toContain('adminExportCompetitionEvents')
  })
})
